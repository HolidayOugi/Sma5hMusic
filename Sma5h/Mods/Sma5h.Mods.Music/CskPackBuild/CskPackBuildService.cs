using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService : ICskPackBuildService
    {
        private const string CskTempFolder = "_csk_temp";
        private const string CloneBgmId = "ui_bgm_a29_ppm_medley";
        private const string CloneSeriesId = "ui_series_mario";
        private const string CloneGameTitleId = "ui_gametitle_paper_mario_series";
        private const string SmashBattlePlaylistId = "bgmsmashbtl";
        private const string SinglePackFolderName = "CSK Music Pack";

        private readonly IOptionsMonitor<CskPackBuildOptions> _config;
        private readonly IMusicModManagerService _musicModManagerService;
        private readonly INus3AudioService _nus3AudioService;
        private readonly IAudioStateService _audioStateService;
        private readonly ILogger _logger;
        private readonly AsyncLocal<string> _currentBuildLocale = new AsyncLocal<string>();

        public CskPackBuildService(
            IOptionsMonitor<CskPackBuildOptions> config,
            IMusicModManagerService musicModManagerService,
            INus3AudioService nus3AudioService,
            IAudioStateService audioStateService,
            ILogger<CskPackBuildService> logger)
        {
            _config = config;
            _musicModManagerService = musicModManagerService;
            _nus3AudioService = nus3AudioService;
            _audioStateService = audioStateService;
            _logger = logger;
        }

        #region Public

        public Task Build(string locale = null)
        {
            return Task.Run(() => BuildInternal(null, CskPackBuildMode.Modular, locale));
        }

        public Task Build(IEnumerable<string> selectedSeriesKeys, string locale = null)
        {
            var selected = new HashSet<string>(selectedSeriesKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
                throw new InvalidOperationException("No CSK pack series were selected.");

            return Task.Run(() => BuildInternal(selected, CskPackBuildMode.Modular, locale));
        }

        public Task BuildMetadataOnly(string locale = null)
        {
            return Task.Run(() => BuildInternal(null, CskPackBuildMode.MetadataOnly, locale));
        }

        public Task BuildSingle(IEnumerable<string> selectedSeriesKeys, string locale = null)
        {
            var selected = new HashSet<string>(selectedSeriesKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
                throw new InvalidOperationException("No CSK pack series were selected.");

            return Task.Run(() => BuildInternal(selected, CskPackBuildMode.Single, locale));
        }

        public Task<IReadOnlyList<CskPackSeriesOption>> GetAvailableSeries(string locale = null)
        {
            return Task.Run<IReadOnlyList<CskPackSeriesOption>>(() =>
            {
                _currentBuildLocale.Value = locale;
                try
                {
                    var contexts = LoadModContexts(GetMusicMods());
                    return contexts
                        .SelectMany(context => context.SeriesList.Select(series => CreateSeriesOption(context, series)))
                        .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                        .ThenBy(p => p.ModName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                }
                finally
                {
                    _currentBuildLocale.Value = null;
                }
            });
        }

        #endregion

        #region Build

        private void BuildInternal(HashSet<string> selectedSeriesKeys, CskPackBuildMode buildMode, string locale)
        {
            _currentBuildLocale.Value = locale;

            try
            {
                var mods = GetMusicMods();
                if (mods.Count == 0)
                    throw new InvalidOperationException("No music mods were found.");

                var contexts = LoadModContexts(mods);
                if (contexts.Count == 0)
                    throw new InvalidOperationException("No metadata_mod.json files were found in the currently loaded music mods.");

                if (selectedSeriesKeys == null)
                {
                    selectedSeriesKeys = contexts
                        .SelectMany(context => context.SeriesList.Select(series => CreateSeriesKey(context.Mod, series)))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                }

                if (selectedSeriesKeys.Count == 0)
                    throw new InvalidOperationException("No CSK pack series were selected.");

                var buildResources = LoadBuildResources();
                var outputRoot = PrepareOutputRoot();
                var tempRoot = Path.Combine(outputRoot, CskTempFolder);

                try
                {
                    var includeAudio = buildMode != CskPackBuildMode.MetadataOnly;
                    var generatedBgmFolder = includeAudio
                        ? GenerateBgmFiles(contexts, tempRoot, selectedSeriesKeys, buildResources.CoreGameOverride)
                        : null;

                    if (buildMode == CskPackBuildMode.Single)
                        GenerateSingleCskPack(contexts, generatedBgmFolder, outputRoot, selectedSeriesKeys, buildResources, includeAudio);
                    else
                        GenerateCskPacks(contexts, generatedBgmFolder, outputRoot, selectedSeriesKeys, buildResources, includeAudio);
                }
                finally
                {
                    if (Directory.Exists(tempRoot))
                        Directory.Delete(tempRoot, true);
                }
            }
            finally
            {
                _currentBuildLocale.Value = null;
            }
        }

        #endregion

        #region Mods

        private List<IMusicMod> GetMusicMods()
        {
            var mods = _musicModManagerService.MusicMods.ToList();
            if (mods.Count == 0)
                mods = _musicModManagerService.RefreshMusicMods().ToList();
            return mods;
        }

        private List<CskModContext> LoadModContexts(IEnumerable<IMusicMod> mods)
        {
            var contexts = new List<CskModContext>();

            foreach (var mod in mods)
            {
                var metadataPath = Path.Combine(mod.ModPath, MusicConstants.MusicModFiles.MUSIC_MOD_METADATA_JSON_FILE);
                if (!File.Exists(metadataPath))
                    continue;

                var metadata = JObject.Parse(File.ReadAllText(metadataPath));
                if (RepairMissingSeriesAndGameMetadata(mod, metadata))
                    metadata = JObject.Parse(File.ReadAllText(metadataPath));

                var packName = GetString(metadata, "name", mod.Name);
                var seriesList = GetArray(metadata, "series").OfType<JObject>().ToList();

                contexts.Add(new CskModContext
                {
                    Mod = mod,
                    MetadataPath = metadataPath,
                    Metadata = metadata,
                    PackName = packName,
                    SafePackName = SanitizePathSegment(packName, mod.Name, "pack folder name"),
                    SeriesList = seriesList,
                    SeriesIdToName = seriesList
                        .Where(p => !string.IsNullOrEmpty(GetString(p, "ui_series_id")) && !string.IsNullOrEmpty(GetString(p, "name_id")))
                        .GroupBy(p => GetString(p, "ui_series_id"), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(p => p.Key, p => GetString(p.First(), "name_id"), StringComparer.OrdinalIgnoreCase)
                });
            }

            return contexts;
        }
        #endregion

        #region Metadata Repair
        private bool RepairMissingSeriesAndGameMetadata(IMusicMod mod, JObject metadata)
        {
            var saved = false;
            var seriesById = _audioStateService.GetSeriesEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiSeriesId))
                .GroupBy(p => p.UiSeriesId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.OrderByDescending(GetSeriesMetadataScore).First(), StringComparer.OrdinalIgnoreCase);
            var gameById = _audioStateService.GetGameTitleEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiGameTitleId))
                .GroupBy(p => p.UiGameTitleId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.OrderByDescending(GetGameMetadataScore).First(), StringComparer.OrdinalIgnoreCase);

            foreach (JObject series in GetArray(metadata, "series").OfType<JObject>())
            {
                var uiSeriesId = GetString(series, "ui_series_id");
                if (NeedsMetadataRepair(series) &&
                    !string.IsNullOrEmpty(uiSeriesId) &&
                    seriesById.TryGetValue(uiSeriesId, out var seriesEntry) &&
                    HasCompleteSeriesMetadata(seriesEntry))
                {
                    var entries = new MusicModEntries();
                    entries.SeriesEntries.Add(seriesEntry);
                    if (!mod.AddOrUpdateMusicModEntries(entries).GetAwaiter().GetResult())
                        throw new InvalidOperationException($"Could not update series metadata for {uiSeriesId} in mod {mod.Name}.");

                    _logger.LogInformation("[CSK] Updated missing series metadata for {UiSeriesId} in mod {ModName}.", uiSeriesId, mod.Name);
                    saved = true;
                }

                foreach (JObject game in GetArray(series, "games").OfType<JObject>())
                {
                    var uiGameTitleId = GetString(game, "ui_gametitle_id");
                    if (!NeedsMetadataRepair(game) ||
                        string.IsNullOrEmpty(uiGameTitleId) ||
                        !gameById.TryGetValue(uiGameTitleId, out var gameEntry) ||
                        !HasCompleteGameMetadata(gameEntry))
                    {
                        continue;
                    }

                    var entries = new MusicModEntries();
                    entries.SeriesEntries.Add(
                        seriesById.TryGetValue(gameEntry.UiSeriesId, out var parentSeriesEntry)
                            ? parentSeriesEntry
                            : CreateSeriesEntryFromMetadata(series));
                    entries.GameTitleEntries.Add(gameEntry);

                    if (!mod.AddOrUpdateMusicModEntries(entries).GetAwaiter().GetResult())
                        throw new InvalidOperationException($"Could not update game title metadata for {uiGameTitleId} in mod {mod.Name}.");

                    _logger.LogInformation("[CSK] Updated missing game title metadata for {UiGameTitleId} in mod {ModName}.", uiGameTitleId, mod.Name);
                    saved = true;
                }
            }

            return saved;
        }

        private static bool NeedsMetadataRepair(JObject entry)
        {
            return string.IsNullOrWhiteSpace(GetString(entry, "name_id")) ||
                   IsNullOrMissing(entry, "msbt_title");
        }

        private static bool IsNullOrMissing(JObject entry, string key)
        {
            if (entry == null || !entry.TryGetValue(key, out var value))
                return true;

            return value == null || value.Type == JTokenType.Null;
        }

        private static bool HasCompleteSeriesMetadata(SeriesEntry seriesEntry)
        {
            return seriesEntry != null &&
                   !string.IsNullOrWhiteSpace(seriesEntry.NameId) &&
                   seriesEntry.MSBTTitle != null;
        }

        private static bool HasCompleteGameMetadata(GameTitleEntry gameEntry)
        {
            return gameEntry != null &&
                   !string.IsNullOrWhiteSpace(gameEntry.NameId) &&
                   gameEntry.MSBTTitle != null;
        }

        private static int GetSeriesMetadataScore(SeriesEntry seriesEntry)
        {
            if (seriesEntry == null)
                return 0;

            return (!string.IsNullOrWhiteSpace(seriesEntry.NameId) ? 1 : 0) +
                   (seriesEntry.MSBTTitle != null ? 1 : 0);
        }

        private static int GetGameMetadataScore(GameTitleEntry gameEntry)
        {
            if (gameEntry == null)
                return 0;

            return (!string.IsNullOrWhiteSpace(gameEntry.NameId) ? 1 : 0) +
                   (gameEntry.MSBTTitle != null ? 1 : 0);
        }

        private static SeriesEntry CreateSeriesEntryFromMetadata(JObject series)
        {
            return new SeriesEntry(GetString(series, "ui_series_id"), EntrySource.Mod)
            {
                NameId = GetString(series, "name_id"),
                DispOrder = ToSByte(GetInt(series, "disp_order", 0)),
                DispOrderSound = ToSByte(GetInt(series, "disp_order_sound", 0)),
                SaveNo = ToSByte(GetInt(series, "save_no", -1)),
                Unk1 = GetBool(series, "0x1c38302364", false),
                IsDlc = GetBool(series, "is_dlc", false),
                IsPatch = GetBool(series, "is_patch", false),
                DlcCharaId = GetString(series, "dlc_chara_id"),
                IsUseAmiiboBg = GetBool(series, "is_use_amiibo_bg", false),
                MSBTTitle = series["msbt_title"]?.ToObject<Dictionary<string, string>>() ?? new Dictionary<string, string>()
            };
        }

        private static sbyte ToSByte(int value)
        {
            if (value < sbyte.MinValue)
                return sbyte.MinValue;
            if (value > sbyte.MaxValue)
                return sbyte.MaxValue;

            return (sbyte)value;
        }

        #endregion

        #region Series Options

        private CskPackSeriesOption CreateSeriesOption(CskModContext context, JObject series)
        {
            return new CskPackSeriesOption
            {
                Key = CreateSeriesKey(context.Mod, series),
                DisplayName = GetSeriesDisplayName(series),
                NameId = GetString(series, "name_id"),
                UiSeriesId = GetString(series, "ui_series_id"),
                ModName = context.Mod.Name
            };
        }

        private static string CreateSeriesKey(IMusicMod mod, JObject series)
        {
            return $"{Path.GetFullPath(mod.ModPath)}|{GetString(series, "ui_series_id")}|{GetString(series, "name_id")}";
        }

        private string GetSeriesDisplayName(JObject series)
        {
            var seriesName = GetString(series, "name_id");
            var title = GetLocalizedString(series["msbt_title"]);
            if (string.IsNullOrWhiteSpace(title))
                title = GetLocalizedString(series["title"]);

            return string.IsNullOrWhiteSpace(title) ? seriesName : title;
        }

        #endregion

    }
}
