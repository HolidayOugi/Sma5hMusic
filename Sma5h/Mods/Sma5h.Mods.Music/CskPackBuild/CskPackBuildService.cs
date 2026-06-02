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

        public Task Build()
        {
            return Task.Run(() => BuildInternal(null, CskPackBuildMode.Modular));
        }

        public Task Build(IEnumerable<string> selectedSeriesKeys)
        {
            var selected = new HashSet<string>(selectedSeriesKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
                throw new InvalidOperationException("No CSK pack series were selected.");

            return Task.Run(() => BuildInternal(selected, CskPackBuildMode.Modular));
        }

        public Task BuildSingle(IEnumerable<string> selectedSeriesKeys)
        {
            var selected = new HashSet<string>(selectedSeriesKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
                throw new InvalidOperationException("No CSK pack series were selected.");

            return Task.Run(() => BuildInternal(selected, CskPackBuildMode.Single));
        }

        public Task<IReadOnlyList<CskPackSeriesOption>> GetAvailableSeries()
        {
            return Task.Run<IReadOnlyList<CskPackSeriesOption>>(() =>
            {
                var contexts = LoadModContexts(GetMusicMods());
                return contexts
                    .SelectMany(context => context.SeriesList.Select(series => CreateSeriesOption(context, series)))
                    .OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(p => p.ModName, StringComparer.OrdinalIgnoreCase)
                .ToList();
            });
        }

        #endregion

        #region Build

        private void BuildInternal(HashSet<string> selectedSeriesKeys, CskPackBuildMode buildMode)
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
                var generatedBgmFolder = GenerateBgmFiles(contexts, tempRoot, selectedSeriesKeys, buildResources.CoreGameOverride);
                if (buildMode == CskPackBuildMode.Single)
                    GenerateSingleCskPack(contexts, generatedBgmFolder, outputRoot, selectedSeriesKeys, buildResources);
                else
                    GenerateCskPacks(contexts, generatedBgmFolder, outputRoot, selectedSeriesKeys, buildResources);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
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
                var packName = GetString(metadata, "name", mod.Name);
                var seriesList = GetArray(metadata, "series").Cast<JObject>().ToList();

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

        private static string GetSeriesDisplayName(JObject series)
        {
            var seriesName = GetString(series, "name_id");
            var title = GetString(series["msbt_title"], "us_en");
            if (string.IsNullOrWhiteSpace(title))
                title = GetString(series["title"], "us_en");

            return string.IsNullOrWhiteSpace(title) ? seriesName : title;
        }

        #endregion

    }
}
