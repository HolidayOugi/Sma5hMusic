using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5hMusic.GUI.Interfaces;
using Sma5hMusic.GUI.Models;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Services
{
    public class CskPackBuildService : ICskPackBuildService
    {
        private const string CoreBgmCsv = "core_bgm.csv";
        private const string VanillaGamesCsv = "vanilla_games.csv";
        private const string CskTempFolder = "_csk_temp";
        private const string CloneBgmId = "ui_bgm_a29_ppm_medley";
        private const string CloneSeriesId = "ui_series_mario";
        private const string CloneGameTitleId = "ui_gametitle_paper_mario_series";

        private readonly IOptionsMonitor<ApplicationSettings> _config;
        private readonly IMusicModManagerService _musicModManagerService;
        private readonly INus3AudioService _nus3AudioService;
        private readonly ILogger _logger;

        public CskPackBuildService(
            IOptionsMonitor<ApplicationSettings> config,
            IMusicModManagerService musicModManagerService,
            INus3AudioService nus3AudioService,
            ILogger<CskPackBuildService> logger)
        {
            _config = config;
            _musicModManagerService = musicModManagerService;
            _nus3AudioService = nus3AudioService;
            _logger = logger;
        }

        public Task Build()
        {
            return Task.Run(() => BuildInternal(null));
        }

        public Task Build(IEnumerable<string> selectedSeriesKeys)
        {
            var selected = new HashSet<string>(selectedSeriesKeys ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (selected.Count == 0)
                throw new InvalidOperationException("No CSK pack series were selected.");

            return Task.Run(() => BuildInternal(selected));
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

        private void BuildInternal(HashSet<string> selectedSeriesKeys)
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
                GenerateCskPacks(contexts, generatedBgmFolder, outputRoot, selectedSeriesKeys, buildResources);
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                    Directory.Delete(tempRoot, true);
            }
        }

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

        private CskBuildResources LoadBuildResources()
        {
            var resourcesPath = _config.CurrentValue.ResourcesPath;
            var overridePath = _config.CurrentValue.Sma5hMusicOverride.ModPath;
            var coreBgmRows = ReadCoreBgmCsv(Path.Combine(resourcesPath, CoreBgmCsv));

            return new CskBuildResources
            {
                CoreBgmRows = coreBgmRows,
                VanillaGames = ReadVanillaGamesCsv(Path.Combine(resourcesPath, VanillaGamesCsv)),
                ToneToSeriesMap = coreBgmRows.ToDictionary(p => $"ui_bgm_{p.ToneId}", p => p.Series, StringComparer.OrdinalIgnoreCase),
                CoreToneIds = coreBgmRows.Select(p => $"ui_bgm_{p.ToneId}").ToHashSet(StringComparer.OrdinalIgnoreCase),
                PlaylistOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_PLAYLIST_JSON_FILE)) ?? new JObject(),
                OrderOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_ORDER_JSON_FILE)),
                CoreBgmOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_CORE_BGM_JSON_FILE)),
                CoreGameOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_CORE_GAME_JSON_FILE)),
                CoreSeriesOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_CORE_SERIES_JSON_FILE)),
                StageOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_STAGE_JSON_FILE))
            };
        }

        private string PrepareOutputRoot()
        {
            var configuredOutputPath = _config.CurrentValue.OutputPath;
            if (string.IsNullOrWhiteSpace(configuredOutputPath))
                throw new InvalidOperationException("Output path is not configured.");

            var outputRoot = Path.GetFullPath(configuredOutputPath);
            if (string.Equals(outputRoot, Path.GetPathRoot(outputRoot), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Refusing to clear the drive root: {outputRoot}");

            _logger.LogInformation("[CSK] Clearing output folder {OutputPath}", outputRoot);
            ClearDirectory(outputRoot);
            return outputRoot;
        }

        private static void ClearDirectory(string path)
        {
            Directory.CreateDirectory(path);

            foreach (var file in Directory.GetFiles(path))
                File.Delete(file);

            foreach (var directory in Directory.GetDirectories(path))
                Directory.Delete(directory, true);
        }

        private string GenerateBgmFiles(IEnumerable<CskModContext> contexts, string tempRoot, HashSet<string> selectedSeriesKeys, JObject coreGameOverride)
        {
            ClearDirectory(tempRoot);

            var outputBgmFolder = Path.Combine(tempRoot, "stream;", "sound", "bgm");
            Directory.CreateDirectory(outputBgmFolder);

            _nus3AudioService.ResetGeneratedNus3BankIds();

            var bgmEntries = contexts
                .SelectMany(context => GetSelectedBgmBuildEntries(context, selectedSeriesKeys, coreGameOverride))
                .Where(p => !string.IsNullOrEmpty(p.NameId) && !string.IsNullOrEmpty(p.Filename))
                .GroupBy(p => p.NameId, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.First())
                .ToList();

            _logger.LogInformation("Generating {Count} nus3audio/nus3bank file(s) for CSK packs.", bgmEntries.Count);

            foreach (var bgmPropertyEntry in bgmEntries)
            {
                var nusBankOutputFile = Path.Combine(outputBgmFolder, string.Format(MusicConstants.GameResources.NUS3BANK_FILE, bgmPropertyEntry.NameId));
                var nusAudioOutputFile = Path.Combine(outputBgmFolder, string.Format(MusicConstants.GameResources.NUS3AUDIO_FILE, bgmPropertyEntry.NameId));

                _logger.LogInformation("Generating Nus3Bank for {NameId}", bgmPropertyEntry.NameId);
                _nus3AudioService.GenerateNus3Bank(bgmPropertyEntry.NameId, bgmPropertyEntry.AudioVolume, nusBankOutputFile);

                if (File.Exists(nusAudioOutputFile))
                    File.Delete(nusAudioOutputFile);

                _logger.LogInformation("Generating or copying Nus3Audio for {NameId}", bgmPropertyEntry.NameId);
                if (!_nus3AudioService.GenerateNus3Audio(bgmPropertyEntry.NameId, bgmPropertyEntry.Filename, nusAudioOutputFile))
                    throw new InvalidOperationException($"The song {bgmPropertyEntry.NameId} could not be processed from {bgmPropertyEntry.Filename}.");
            }

            return outputBgmFolder;
        }

        private IEnumerable<BgmBuildEntry> GetSelectedBgmBuildEntries(CskModContext context, HashSet<string> selectedSeriesKeys, JObject coreGameOverride)
        {
            foreach (var series in context.SeriesList.Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series))))
            {
                foreach (JObject game in GetArray(series, "games"))
                {
                    foreach (JObject bgm in GetArray(game, "bgms"))
                        yield return CreateBgmBuildEntry(context.Mod.ModPath, bgm);
                }

                foreach (var movedGame in GetCoreGameMovedGames(series, context.Metadata, coreGameOverride))
                {
                    foreach (JObject bgm in GetArray(movedGame, "bgms"))
                        yield return CreateBgmBuildEntry(context.Mod.ModPath, bgm);
                }
            }
        }

        private BgmBuildEntry CreateBgmBuildEntry(string modPath, JObject bgm)
        {
            var bgmProperty = bgm["bgm_properties"] as JObject;
            var nameId = GetString(bgmProperty, "name_id");
            var filename = GetString(bgm, "filename");

            return new BgmBuildEntry
            {
                NameId = nameId,
                Filename = Path.Combine(modPath, filename),
                AudioVolume = GetFloat(bgm["nus3bank_config"], "volume", 2.7f)
            };
        }

        private void GenerateCskPacks(IEnumerable<CskModContext> contexts, string generatedBgmFolder, string outputRoot, HashSet<string> selectedSeriesKeys, CskBuildResources buildResources)
        {
            foreach (var context in contexts)
            {
                _logger.LogInformation("Generating CSK packs from {MetadataPath}", context.MetadataPath);

                var seriesSoundOrder = BuildSeriesSoundOrder(context.SeriesList, buildResources.OrderOverride, buildResources.CoreBgmRows);

                foreach (var series in context.SeriesList.Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series))))
                {
                    var savedPath = ProcessSeries(
                        series,
                        context.SafePackName,
                        outputRoot,
                        generatedBgmFolder,
                        buildResources.PlaylistOverride,
                        context.SeriesIdToName,
                        buildResources.CoreBgmOverride,
                        buildResources.ToneToSeriesMap,
                        buildResources.OrderOverride,
                        buildResources.VanillaGames,
                        seriesSoundOrder,
                        buildResources.StageOverride,
                        buildResources.CoreGameOverride,
                        buildResources.CoreSeriesOverride,
                        context.Metadata,
                        buildResources.CoreToneIds);

                    _logger.LogInformation("[CSK] Saved {SeriesName}: {SavedPath}", GetString(series, "name_id", "<unknown>"), savedPath);
                }
            }
        }

        private Dictionary<string, int> BuildSeriesSoundOrder(List<JObject> seriesList, JObject orderOverride, List<CoreBgmRow> coreBgmRows)
        {
            if (orderOverride != null)
            {
                var seriesMinOverride = new List<Tuple<string, int>>();

                foreach (var series in seriesList)
                {
                    var uiSeriesId = GetString(series, "ui_series_id");
                    var nameId = uiSeriesId.StartsWith("ui_series_", StringComparison.OrdinalIgnoreCase)
                        ? uiSeriesId.Substring("ui_series_".Length)
                        : GetString(series, "name_id");
                    var bgmIdsToCheck = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    int? minValue = null;

                    foreach (JObject game in GetArray(series, "games"))
                    {
                        foreach (JObject bgm in GetArray(game, "bgms"))
                        {
                            var dbRoot = bgm["db_root"] as JObject;
                            var uiBgmId = GetString(dbRoot, "ui_bgm_id");
                            if (!string.IsNullOrEmpty(uiBgmId))
                                bgmIdsToCheck.Add(uiBgmId);
                        }
                    }

                    if (VanillaSeries.Contains(nameId) || nameId == "classic_sonic")
                    {
                        foreach (var row in coreBgmRows.Where(p => string.Equals(p.Series, nameId, StringComparison.OrdinalIgnoreCase)))
                            bgmIdsToCheck.Add($"ui_bgm_{row.ToneId}");
                    }

                    foreach (var uiBgmId in bgmIdsToCheck)
                    {
                        var value = GetInt(orderOverride, uiBgmId, int.MinValue);
                        if (value == int.MinValue || value == -1)
                            continue;

                        if (!minValue.HasValue || value < minValue.Value)
                            minValue = value;
                    }

                    seriesMinOverride.Add(Tuple.Create(nameId, minValue ?? int.MaxValue));
                }

                return seriesMinOverride
                    .OrderBy(p => p.Item2)
                    .Select((p, i) => new { p.Item1, Index = i })
                    .ToDictionary(p => p.Item1, p => p.Index, StringComparer.OrdinalIgnoreCase);
            }

            return seriesList
                .Where(p => !VanillaSeries.Contains(GetString(p, "name_id")))
                .OrderBy(p => GetSeriesDisplayName(p).ToLowerInvariant())
                .Select((p, i) => new { NameId = GetString(p, "name_id"), Order = 39 + i })
                .ToDictionary(p => p.NameId, p => p.Order, StringComparer.OrdinalIgnoreCase);
        }

        private string ProcessSeries(
            JObject series,
            string packName,
            string outputRoot,
            string generatedBgmFolder,
            JObject playlistOverride,
            Dictionary<string, string> seriesIdToName,
            JObject coreBgmOverride,
            Dictionary<string, string> toneToSeriesMap,
            JObject orderOverride,
            Dictionary<string, string> vanillaGames,
            Dictionary<string, int> seriesSoundOrder,
            JObject stageOverride,
            JObject coreGameOverride,
            JObject coreSeriesOverride,
            JObject metadata,
            HashSet<string> coreToneIds)
        {
            var orderCounter = 0;
            var seriesName = GetString(series, "name_id");
            var realName = GetSeriesDisplayName(series);
            var safeSeriesName = SanitizePathSegment(realName, seriesName, "series folder name");
            var seriesFolderName = SanitizePathSegment($"{packName} - {safeSeriesName}", seriesName, "full series folder name");
            var seriesTitle = GetString(series["msbt_title"], "us_en", seriesName);

            if (coreSeriesOverride != null)
            {
                var overrideEntry = coreSeriesOverride[GetString(series, "ui_series_id")] as JObject;
                if (overrideEntry != null)
                    seriesTitle = GetString(overrideEntry["msbt_title"], "us_en", seriesTitle);
            }

            var seriesDbFolder = Path.Combine(outputRoot, seriesFolderName, "database");
            var seriesUiFolder = Path.Combine(outputRoot, seriesFolderName, "ui", "message");
            Directory.CreateDirectory(seriesDbFolder);
            Directory.CreateDirectory(seriesUiFolder);

            var songData = CreateSongData();
            var msgBgmEntries = new List<string>();
            var msgTitleEntries = new List<string>();

            if (orderOverride != null || !VanillaSeries.Contains(seriesName) || seriesName.StartsWith("etc", StringComparison.OrdinalIgnoreCase))
            {
                var dispOrderSound = seriesSoundOrder.ContainsKey(seriesName) ? seriesSoundOrder[seriesName] : 0;
                if (dispOrderSound > 127)
                    dispOrderSound = 127;

                var isDlcSeries = DlcSeries.Contains(seriesName);
                GetArray(songData, "series_database_entries").Add(new JObject
                {
                    ["ui_series_id"] = GetString(series, "ui_series_id"),
                    ["clone_from_series_id"] = CloneSeriesId,
                    ["name_id"] = seriesName,
                    ["disp_order"] = 0,
                    ["disp_order_sound"] = dispOrderSound,
                    ["save_no"] = 0,
                    ["shown_as_series_in_directory"] = false,
                    ["is_dlc"] = isDlcSeries,
                    ["is_patch"] = isDlcSeries,
                    ["is_use_amiibo_bg"] = false
                });
            }

            msgTitleEntries.Add(MakeEntry($"tit_series_snd_{seriesName}", EscapeXml(seriesTitle)));
            msgTitleEntries.Add(MakeEntry($"tit_series_{seriesName}", EscapeXml(seriesTitle)));

            foreach (JObject game in GetArray(series, "games"))
            {
                if (coreGameOverride != null)
                {
                    var gameOverride = coreGameOverride[GetString(game, "ui_gametitle_id")] as JObject;
                    if (gameOverride != null && GetString(gameOverride, "ui_series_id") != GetString(series, "ui_series_id"))
                        continue;
                }

                var gameName = GetString(game, "name_id");
                if (!vanillaGames.ContainsKey(gameName) || vanillaGames[gameName] != seriesName)
                    AddGameTitleEntry(songData, game);

                var gameTitle = GetString(game["msbt_title"], "us_en", gameName);
                msgTitleEntries.Add(MakeEntry($"tit_{gameName}", EscapeXml(gameTitle)));

                foreach (JObject bgm in GetArray(game, "bgms"))
                    orderCounter = ProcessBgm(bgm, songData, playlistOverride, msgBgmEntries, orderOverride, seriesName, seriesFolderName, outputRoot, generatedBgmFolder, orderCounter);
            }

            ProcessCoreGameMovedBgms(series, metadata, coreGameOverride, songData, playlistOverride, msgBgmEntries, msgTitleEntries, orderOverride, seriesName, seriesFolderName, outputRoot, generatedBgmFolder, ref orderCounter);
            ProcessPlaylistsAndStages(songData, seriesName, playlistOverride, stageOverride, coreToneIds);
            ProcessCoreBgmOverrides(songData, msgBgmEntries, msgTitleEntries, seriesName, seriesIdToName, coreBgmOverride, toneToSeriesMap, orderOverride, coreGameOverride);
            AddCoreGameOverridesForNewSeries(songData, series, seriesName, coreGameOverride);

            var outputJsonPath = Path.Combine(seriesDbFolder, $"{SanitizePathSegment(seriesName, "series", "series database file name")}.json");
            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(songData, Formatting.Indented), new UTF8Encoding(false));
            WriteXmsbt(Path.Combine(seriesUiFolder, "msg_bgm.xmsbt"), msgBgmEntries);
            WriteXmsbt(Path.Combine(seriesUiFolder, "msg_title.xmsbt"), msgTitleEntries);
            return outputJsonPath;
        }

        private int ProcessBgm(
            JObject bgm,
            JObject songData,
            JObject playlistOverride,
            List<string> msgBgmEntries,
            JObject orderOverride,
            string seriesName,
            string seriesFolderName,
            string outputRoot,
            string generatedBgmFolder,
            int orderCounter)
        {
            var db = bgm["db_root"] as JObject;
            var assigned = bgm["assigned_info"] as JObject;
            var streamProp = bgm["stream_property"] as JObject;
            var bgmProp = bgm["bgm_properties"] as JObject;
            var streamSet = bgm["stream_set"] as JObject;

            var uiBgmId = GetString(db, "ui_bgm_id");
            var testDispOrder = orderOverride != null ? GetInt(orderOverride, uiBgmId, GetInt(db, "test_disp_order", 0)) : 0;

            GetArray(songData, "bgm_database_entries").Add(new JObject
            {
                ["ui_bgm_id"] = uiBgmId,
                ["clone_from_ui_bgm_id"] = CloneBgmId,
                ["stream_set_id"] = GetString(db, "stream_set_id"),
                ["name_id"] = GetString(bgmProp, "name_id"),
                ["ui_gametitle_id"] = GetString(db, "ui_gametitle_id"),
                ["test_disp_order"] = testDispOrder,
                ["record_type"] = GetString(db, "record_type", "record_original")
            });

            GetArray(songData, "stream_set_entries").Add(CreateStreamSetEntry(streamSet));
            GetArray(songData, "assigned_info_entries").Add(new JObject
            {
                ["info_id"] = GetString(assigned, "info_id"),
                ["stream_id"] = GetString(assigned, "stream_id"),
                ["condition"] = GetString(assigned, "condition"),
                ["condition_process"] = "sound_condition_process_add",
                ["change_fadeout_frame"] = 60,
                ["menu_change_fadeout_frame"] = 60
            });

            GetArray(songData, "stream_property_entries").Add(new JObject
            {
                ["stream_id"] = GetString(streamProp, "stream_id"),
                ["data_name0"] = GetString(streamProp, "data_name0")
            });

            GetArray(songData, "bgm_property_entries").Add(new JObject
            {
                ["stream_name"] = GetString(streamProp, "data_name0"),
                ["loop_start_ms"] = GetInt(bgmProp, "loop_start_ms", 0),
                ["loop_start_sample"] = GetInt(bgmProp, "loop_start_sample", 0),
                ["loop_end_ms"] = GetInt(bgmProp, "loop_end_ms", 0),
                ["loop_end_sample"] = GetInt(bgmProp, "loop_end_sample", 0),
                ["duration_ms"] = GetInt(bgmProp, "total_time_ms", 0),
                ["duration_sample"] = GetInt(bgmProp, "total_samples", 0)
            });

            orderCounter = AddToPlaylists(uiBgmId, songData, playlistOverride, seriesName, orderCounter);

            var nameId = GetString(bgmProp, "name_id");
            var titleText = GetString(db["msbt_title"], "us_en", nameId);
            msgBgmEntries.Add(MakeEntry($"bgm_title_{nameId}", EscapeXml(titleText)));

            var authorText = GetString(db["msbt_author"], "us_en");
            if (!string.IsNullOrEmpty(authorText))
                msgBgmEntries.Add(MakeEntry($"bgm_author_{nameId}", EscapeXml(authorText)));

            var copyrightText = GetString(db["msbt_copyright"], "us_en");
            if (!string.IsNullOrEmpty(copyrightText))
                msgBgmEntries.Add(MakeEntry($"bgm_copyright_{nameId}", EscapeXml(copyrightText)));

            CopyBgmFiles(bgm, seriesFolderName, outputRoot, generatedBgmFolder);
            return orderCounter;
        }

        private void ProcessCoreGameMovedBgms(
            JObject series,
            JObject metadata,
            JObject coreGameOverride,
            JObject songData,
            JObject playlistOverride,
            List<string> msgBgmEntries,
            List<string> msgTitleEntries,
            JObject orderOverride,
            string seriesName,
            string seriesFolderName,
            string outputRoot,
            string generatedBgmFolder,
            ref int orderCounter)
        {
            if (coreGameOverride == null)
                return;

            foreach (var gameMeta in GetCoreGameMovedGames(series, metadata, coreGameOverride))
            {
                var gameTitle = GetString(gameMeta["msbt_title"], "us_en", GetString(gameMeta, "name_id"));
                msgTitleEntries.Add(MakeEntry($"tit_{GetString(gameMeta, "name_id")}", EscapeXml(gameTitle)));

                foreach (JObject bgm in GetArray(gameMeta, "bgms"))
                    orderCounter = ProcessBgm(bgm, songData, playlistOverride, msgBgmEntries, orderOverride, seriesName, seriesFolderName, outputRoot, generatedBgmFolder, orderCounter);
            }
        }

        private IEnumerable<JObject> GetCoreGameMovedGames(JObject series, JObject metadata, JObject coreGameOverride)
        {
            if (coreGameOverride == null)
                yield break;

            foreach (var overrideProperty in coreGameOverride.Properties())
            {
                var overrideEntry = overrideProperty.Value as JObject;
                if (GetString(overrideEntry, "ui_series_id") != GetString(series, "ui_series_id"))
                    continue;

                var movedGame = FindCoreGameMovedGame(metadata, overrideEntry);
                if (movedGame != null)
                    yield return movedGame;
            }
        }

        private JObject FindCoreGameMovedGame(JObject metadata, JObject overrideEntry)
        {
            foreach (JObject metaSeries in GetArray(metadata, "series"))
            {
                foreach (JObject game in GetArray(metaSeries, "games"))
                {
                    if (GetString(game, "ui_gametitle_id") == GetString(overrideEntry, "ui_gametitle_id") &&
                        GetString(game, "ui_series_id") != GetString(overrideEntry, "ui_series_id"))
                    {
                        return game;
                    }
                }
            }

            return null;
        }

        private void ProcessCoreBgmOverrides(
            JObject songData,
            List<string> msgBgmEntries,
            List<string> msgTitleEntries,
            string seriesName,
            Dictionary<string, string> seriesIdToName,
            JObject coreBgmOverride,
            Dictionary<string, string> toneToSeriesMap,
            JObject orderOverride,
            JObject coreGameOverride)
        {
            if (coreBgmOverride == null)
                return;

            var alreadyAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dbRoots = coreBgmOverride["CoreBgmDbRootOverrides"] as JObject ?? new JObject();
            var streamSets = coreBgmOverride["CoreBgmStreamSetOverrides"] as JObject ?? new JObject();
            var assignedInfos = coreBgmOverride["CoreBgmAssignedInfoOverrides"] as JObject ?? new JObject();

            foreach (var dbProperty in dbRoots.Properties())
            {
                var uiBgmId = dbProperty.Name;
                var db = dbProperty.Value as JObject;
                var dbSeriesId = GetString(db, "ui_series_id");
                var coreSeries = toneToSeriesMap.ContainsKey(uiBgmId)
                    ? toneToSeriesMap[uiBgmId]
                    : seriesIdToName.ContainsKey(dbSeriesId) ? seriesIdToName[dbSeriesId] : null;

                if (coreSeries != seriesName)
                    continue;

                var streamSetId = GetString(db, "stream_set_id");
                var streamSetData = streamSets[streamSetId] as JObject ?? new JObject();
                var testDispOrder = orderOverride != null ? GetInt(orderOverride, uiBgmId, GetInt(db, "test_disp_order", 0)) : 0;
                var uiGameTitleId = GetString(db, "ui_gametitle_id");
                var nameId = GetString(db, "name_id", uiBgmId);

                GetArray(songData, "bgm_database_entries").Add(new JObject
                {
                    ["ui_bgm_id"] = uiBgmId,
                    ["clone_from_ui_bgm_id"] = CloneBgmId,
                    ["stream_set_id"] = streamSetId,
                    ["name_id"] = nameId,
                    ["ui_gametitle_id"] = uiGameTitleId,
                    ["test_disp_order"] = testDispOrder,
                    ["record_type"] = GetString(db, "record_type", "record_original")
                });

                GetArray(songData, "stream_set_entries").Add(CreateStreamSetEntry(streamSetData, streamSetId));

                var info0Key = GetString(streamSetData, "info0");
                var assigned = assignedInfos[info0Key] as JObject;
                if (assigned != null)
                {
                    GetArray(songData, "assigned_info_entries").Add(new JObject
                    {
                        ["info_id"] = GetString(assigned, "info_id"),
                        ["stream_id"] = GetString(assigned, "stream_id"),
                        ["condition"] = GetString(assigned, "condition"),
                        ["condition_process"] = GetString(assigned, "condition_process", "sound_condition_process_add"),
                        ["change_fadeout_frame"] = GetInt(assigned, "change_fadeout_frame", 60),
                        ["menu_change_fadeout_frame"] = GetInt(assigned, "menu_change_fadeout_frame", 60)
                    });
                }

                AddOptionalBgmMessage(msgBgmEntries, $"bgm_title_{nameId}", db["msbt_title"]);
                AddOptionalBgmMessage(msgBgmEntries, $"bgm_author_{nameId}", db["msbt_author"]);
                AddOptionalBgmMessage(msgBgmEntries, $"bgm_copyright_{nameId}", db["msbt_copyright"]);

                if (coreGameOverride == null || string.IsNullOrEmpty(uiGameTitleId))
                    continue;

                var game = coreGameOverride[uiGameTitleId] as JObject;
                var gameTitle = GetString(game?["msbt_title"], "us_en", GetString(game?["msbt_title"], "eu_en"));
                if (!string.IsNullOrEmpty(gameTitle))
                {
                    var entryId = $"tit_{GetString(game, "name_id")}";
                    if (alreadyAdded.Add(entryId))
                        msgTitleEntries.Add(MakeEntry(entryId, EscapeXml(gameTitle)));
                }
            }
        }

        private void ProcessPlaylistsAndStages(JObject songData, string seriesName, JObject playlistOverride, JObject stageOverride, HashSet<string> coreToneIds)
        {
            if (VanillaSeries.Contains(seriesName))
            {
                PopulateVanillaPlaylists(songData, seriesName, playlistOverride, coreToneIds);
                if (stageOverride != null)
                    PopulateStageDatabaseEntries(songData, seriesName, stageOverride, playlistOverride);
                return;
            }

            if (stageOverride == null)
                return;

            var validPlaylists = SeriesToPlaylist.Values.SelectMany(p => p).ToHashSet(StringComparer.OrdinalIgnoreCase);
            validPlaylists.Add("bgmsmashmenu");

            foreach (var playlistName in ((JObject)songData["playlist_entries"]).Properties().Select(p => p.Name).ToList())
            {
                if (validPlaylists.Contains(playlistName))
                    continue;

                var foundStage = stageOverride.Properties()
                    .FirstOrDefault(p => GetString(p.Value, "bgm_set_id") == playlistName);

                if (foundStage != null)
                {
                    GetArray(songData, "stage_database_entries").Add(new JObject
                    {
                        ["ui_stage_id"] = foundStage.Name,
                        ["bgm_set_id"] = playlistName
                    });
                }
            }
        }

        private void PopulateStageDatabaseEntries(JObject songData, string seriesName, JObject stageOverride, JObject playlistOverride)
        {
            var seriesKey = seriesName.ToLowerInvariant();
            if (!SeriesToPlaylist.ContainsKey(seriesKey))
                return;

            var validPlaylists = SeriesToPlaylist[seriesKey];
            var defaultPlaylist = validPlaylists[0];
            var validUiSeries = seriesKey == "etc"
                ? new HashSet<string>(new[]
                {
                    "ui_series_etc", "ui_series_nintendogs", "ui_series_balloonfight",
                    "ui_series_duckhunt", "ui_series_plankton", "ui_series_iceclimber",
                    "ui_series_touch", "ui_series_lightplane", "ui_series_miiplaza",
                    "ui_series_tomodachi", "ui_series_wuhuisland", "ui_series_wreckingcrew"
                }, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(new[] { $"ui_series_{seriesKey}" }, StringComparer.OrdinalIgnoreCase);

            var stageSeriesOverride = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ui_stage_kart_circuitfor"] = "mariokart",
                ["ui_stage_kart_circuitx"] = "mariokart"
            };

            foreach (var stageProperty in stageOverride.Properties())
            {
                var stageId = stageProperty.Name;
                var stageData = stageProperty.Value;
                var uiSeriesId = GetString(stageData, "ui_series_id");
                var validPlaylistsStage = validPlaylists;
                var defaultPlaylistStage = defaultPlaylist;
                var validUiSeriesStage = validUiSeries;
                var uiSeriesIdCheck = uiSeriesId;

                if (stageSeriesOverride.ContainsKey(stageId))
                {
                    var forcedSeriesKey = stageSeriesOverride[stageId];
                    if (seriesKey != forcedSeriesKey || !SeriesToPlaylist.ContainsKey(forcedSeriesKey))
                        continue;

                    uiSeriesIdCheck = $"ui_series_{forcedSeriesKey}";
                    validPlaylistsStage = SeriesToPlaylist[forcedSeriesKey];
                    defaultPlaylistStage = validPlaylistsStage[0];
                    validUiSeriesStage = new HashSet<string>(new[] { uiSeriesIdCheck }, StringComparer.OrdinalIgnoreCase);
                }

                if (!validUiSeriesStage.Contains(uiSeriesIdCheck))
                    continue;

                var bgmSetId = GetString(stageData, "bgm_set_id");
                if (string.IsNullOrEmpty(bgmSetId))
                    continue;

                var chosenBgm = validPlaylistsStage.Contains(bgmSetId) || playlistOverride[bgmSetId] != null
                    ? bgmSetId
                    : defaultPlaylistStage;

                GetArray(songData, "stage_database_entries").Add(new JObject
                {
                    ["ui_stage_id"] = stageId,
                    ["bgm_set_id"] = chosenBgm
                });
            }
        }

        private void PopulateVanillaPlaylists(JObject songData, string seriesName, JObject playlistOverride, HashSet<string> coreToneIds)
        {
            var playlists = SeriesToPlaylist.ContainsKey(seriesName.ToLowerInvariant())
                ? SeriesToPlaylist[seriesName.ToLowerInvariant()]
                : new List<string>();

            foreach (var playlistId in playlists)
            {
                var playlistEntries = EnsurePlaylist(songData, playlistId);
                var tracks = GetArray(playlistOverride[playlistId], "tracks");

                foreach (JObject track in tracks)
                {
                    var uiBgmId = GetString(track, "ui_bgm_id");
                    if (!coreToneIds.Contains(uiBgmId))
                        continue;

                    var entry = new JObject { ["ui_bgm_id"] = uiBgmId };
                    for (var i = 0; i < 16; i++)
                    {
                        entry[$"order{i}"] = GetInt(track, $"o{i}", 0);
                        entry[$"incidence{i}"] = GetInt(track, $"i{i}", 10000);
                    }

                    playlistEntries.Add(entry);
                }
            }
        }

        private int AddToPlaylists(string uiBgmId, JObject songData, JObject playlistOverride, string seriesName, int orderCounter)
        {
            var found = false;
            foreach (var playlistProperty in playlistOverride.Properties())
            {
                var playlistId = playlistProperty.Name;
                foreach (JObject track in GetArray(playlistProperty.Value, "tracks"))
                {
                    if (GetString(track, "ui_bgm_id") != uiBgmId)
                        continue;

                    found = true;
                    var entries = EnsurePlaylist(songData, playlistId);
                    if (entries.Any(p => GetString(p, "ui_bgm_id") == uiBgmId))
                        continue;

                    var entry = new JObject { ["ui_bgm_id"] = uiBgmId };
                    for (var i = 0; i < 16; i++)
                    {
                        entry[$"order{i}"] = GetInt(track, $"o{i}", orderCounter);
                        entry[$"incidence{i}"] = GetInt(track, $"i{i}", 10000);
                    }

                    entries.Add(entry);
                }
            }

            var currentEntry = GetArray(songData, "bgm_database_entries")
                .FirstOrDefault(p => GetString(p, "ui_bgm_id") == uiBgmId) as JObject;

            if (!found && currentEntry != null && GetInt(currentEntry, "test_disp_order", -1) != -1)
            {
                var playlists = SeriesToPlaylist.ContainsKey(seriesName.ToLowerInvariant())
                    ? SeriesToPlaylist[seriesName.ToLowerInvariant()]
                    : new List<string> { $"bgm{seriesName}" };

                foreach (var fallbackPlaylistId in playlists)
                {
                    var entries = EnsurePlaylist(songData, fallbackPlaylistId);
                    var entry = new JObject { ["ui_bgm_id"] = uiBgmId };
                    for (var i = 0; i < 16; i++)
                    {
                        entry[$"order{i}"] = orderCounter;
                        entry[$"incidence{i}"] = 10000;
                    }

                    entries.Add(entry);
                    orderCounter++;
                }
            }

            return orderCounter;
        }

        private void AddCoreGameOverridesForNewSeries(JObject songData, JObject series, string seriesName, JObject coreGameOverride)
        {
            if (coreGameOverride == null || VanillaSeries.Contains(seriesName))
                return;

            var expectedUiSeriesId = GetString(series, "ui_series_id");
            foreach (var overrideProperty in coreGameOverride.Properties())
            {
                var overrideEntry = overrideProperty.Value as JObject;
                if (GetString(overrideEntry, "ui_series_id") != expectedUiSeriesId)
                    continue;

                var newEntry = new JObject
                {
                    ["ui_gametitle_id"] = overrideProperty.Name,
                    ["clone_from_gametitle_id"] = CloneGameTitleId,
                    ["name_id"] = GetString(overrideEntry, "name_id"),
                    ["ui_series_id"] = GetString(overrideEntry, "ui_series_id"),
                    ["shown_as_series_in_directory"] = false
                };

                var entries = GetArray(songData, "gametitle_database_entries");
                if (!entries.Any(p => JToken.DeepEquals(p, newEntry)))
                    entries.Add(newEntry);
            }
        }

        private void AddGameTitleEntry(JObject songData, JObject game)
        {
            GetArray(songData, "gametitle_database_entries").Add(new JObject
            {
                ["ui_gametitle_id"] = GetString(game, "ui_gametitle_id"),
                ["clone_from_gametitle_id"] = CloneGameTitleId,
                ["name_id"] = GetString(game, "name_id"),
                ["ui_series_id"] = GetString(game, "ui_series_id"),
                ["shown_as_series_in_directory"] = false
            });
        }

        private JObject CreateStreamSetEntry(JObject streamSet, string streamSetId = null)
        {
            var entry = new JObject { ["stream_set_id"] = streamSetId ?? GetString(streamSet, "stream_set_id") };
            for (var i = 0; i < 16; i++)
            {
                var key = $"info{i}";
                var value = GetString(streamSet, key);
                if (!string.IsNullOrEmpty(value))
                    entry[key] = value;
            }

            var specialCategory = GetString(streamSet, "special_category");
            if (!string.IsNullOrEmpty(specialCategory))
                entry["special_category"] = specialCategory;

            return entry;
        }

        private void CopyBgmFiles(JObject bgm, string seriesFolderName, string outputRoot, string generatedBgmFolder)
        {
            var filename = GetString(bgm, "filename");
            var filenameNoExt = Path.GetFileNameWithoutExtension(filename);
            var nus3AudioSrc = Path.Combine(generatedBgmFolder, $"bgm_{filenameNoExt}.nus3audio");
            var nus3BankSrc = Path.Combine(generatedBgmFolder, $"bgm_{filenameNoExt}.nus3bank");
            var destFolder = Path.Combine(outputRoot, seriesFolderName, "stream;", "sound", "bgm");
            Directory.CreateDirectory(destFolder);

            CopyIfExists(nus3AudioSrc, Path.Combine(destFolder, Path.GetFileName(nus3AudioSrc)));
            CopyIfExists(nus3BankSrc, Path.Combine(destFolder, Path.GetFileName(nus3BankSrc)));
        }

        private void CopyIfExists(string source, string destination)
        {
            if (File.Exists(source))
            {
                File.Copy(source, destination, true);
            }
            else
            {
                _logger.LogWarning("[CSK] File missing: {Source}", source);
            }
        }

        private JArray EnsurePlaylist(JObject songData, string playlistId)
        {
            var playlists = songData["playlist_entries"] as JObject;
            if (playlists[playlistId] == null)
                playlists[playlistId] = new JArray();
            return (JArray)playlists[playlistId];
        }

        private static JObject CreateSongData()
        {
            return new JObject
            {
                ["series_database_entries"] = new JArray(),
                ["gametitle_database_entries"] = new JArray(),
                ["bgm_database_entries"] = new JArray(),
                ["stream_set_entries"] = new JArray(),
                ["assigned_info_entries"] = new JArray(),
                ["stream_property_entries"] = new JArray(),
                ["bgm_property_entries"] = new JArray(),
                ["playlist_entries"] = new JObject(),
                ["stage_database_entries"] = new JArray()
            };
        }

        private JObject LoadJsonObject(string path)
        {
            if (!File.Exists(path))
                return null;

            _logger.LogInformation("Loading {Path}", path);
            return JObject.Parse(File.ReadAllText(path));
        }

        private List<CoreBgmRow> ReadCoreBgmCsv(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Required CSV not found: {path}", path);

            var rows = ReadCsv(path);
            return rows.Select(p => new CoreBgmRow
            {
                ToneId = p.ContainsKey("Tone ID") ? p["Tone ID"] : string.Empty,
                Series = p.ContainsKey("Series") ? p["Series"] : string.Empty
            }).Where(p => !string.IsNullOrEmpty(p.ToneId)).ToList();
        }

        private Dictionary<string, string> ReadVanillaGamesCsv(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Required CSV not found: {path}", path);

            return ReadCsv(path)
                .Where(p => p.ContainsKey("game") && p.ContainsKey("series"))
                .GroupBy(p => p["game"], StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First()["series"], StringComparer.OrdinalIgnoreCase);
        }

        private static List<Dictionary<string, string>> ReadCsv(string path)
        {
            var lines = File.ReadAllLines(path);
            if (lines.Length == 0)
                return new List<Dictionary<string, string>>();

            var headers = SplitCsvLine(lines[0]).ToList();
            var rows = new List<Dictionary<string, string>>();
            for (var i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i]))
                    continue;

                var values = SplitCsvLine(lines[i]).ToList();
                var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var h = 0; h < headers.Count; h++)
                    row[headers[h]] = h < values.Count ? values[h] : string.Empty;
                rows.Add(row);
            }

            return rows;
        }

        private static IEnumerable<string> SplitCsvLine(string line)
        {
            var output = new List<string>();
            var current = new StringBuilder();
            var inQuotes = false;

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];
                if (c == '"')
                {
                    if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = !inQuotes;
                    }
                }
                else if (c == ',' && !inQuotes)
                {
                    output.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }

            output.Add(current.ToString());
            return output;
        }

        private static void AddOptionalBgmMessage(List<string> entries, string label, JToken localizedText)
        {
            var text = GetString(localizedText, "us_en");
            if (!string.IsNullOrEmpty(text))
                entries.Add(MakeEntry(label, EscapeXml(text)));
        }

        private static string MakeEntry(string label, string text)
        {
            return $"<entry label=\"{label}\">\r\n<text>{text}</text>\r\n</entry>";
        }

        private static void WriteXmsbt(string path, IEnumerable<string> entries)
        {
            var content = new StringBuilder();
            content.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\n<xmsbt>\n");
            foreach (var entry in entries)
                content.Append(entry).Append("\n");
            content.Append("</xmsbt>");
            File.WriteAllText(path, content.ToString(), Encoding.Unicode);
        }

        private static string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            text = Regex.Replace(text, "\\{\\{(.*?)\\}\\}", "$1");
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("'", "&apos;")
                .Replace("\"", "&quot;");
        }

        private static string GetString(JToken token, string key, string fallback = "")
        {
            if (token == null)
                return fallback;

            var value = token[key];
            return value == null || value.Type == JTokenType.Null ? fallback : value.ToString();
        }

        private static int GetInt(JToken token, string key, int fallback)
        {
            if (token == null)
                return fallback;

            var value = token[key];
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            int output;
            return int.TryParse(value.ToString(), out output) ? output : fallback;
        }

        private static float GetFloat(JToken token, string key, float fallback)
        {
            if (token == null)
                return fallback;

            var value = token[key];
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            float output;
            return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out output) ? output : fallback;
        }

        private static JArray GetArray(JToken token, string key)
        {
            if (token == null)
                return new JArray();

            return token[key] as JArray ?? new JArray();
        }

        private string SanitizePathSegment(string value, string fallback, string context)
        {
            var sanitized = SanitizePathSegmentValue(value, fallback);

            if (!string.Equals(value, sanitized, StringComparison.Ordinal))
                _logger.LogWarning("[CSK] Sanitized {Context}: '{Original}' -> '{Sanitized}'", context, value, sanitized);

            return sanitized;
        }

        private static string SanitizePathSegmentValue(string value, string fallback)
        {
            var text = string.IsNullOrWhiteSpace(value) ? fallback : value;
            if (string.IsNullOrWhiteSpace(text))
                text = "unnamed";

            var invalidChars = new HashSet<char>(new[]
            {
                '<', '>', ':', '\"', '/', '\\', '|', '?', '*'
            });

            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (!invalidChars.Contains(c) && !char.IsControl(c))
                    builder.Append(c);
            }

            var sanitized = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
            sanitized = sanitized.TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = "unnamed";

            if (IsReservedWindowsName(sanitized))
                sanitized = $"_{sanitized}";

            return sanitized;
        }

        private static bool IsReservedWindowsName(string value)
        {
            var name = value.Split('.')[0].Trim();
            if (string.IsNullOrEmpty(name))
                return false;

            var reservedNames = new HashSet<string>(new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            }, StringComparer.OrdinalIgnoreCase);

            return reservedNames.Contains(name);
        }

        private static string GetSeriesDisplayName(JObject series)
        {
            var seriesName = GetString(series, "name_id");
            var title = GetString(series["msbt_title"], "us_en");
            if (string.IsNullOrWhiteSpace(title))
                title = GetString(series["title"], "us_en");

            return string.IsNullOrWhiteSpace(title) ? seriesName : title;
        }

        private class CoreBgmRow
        {
            public string ToneId { get; set; }
            public string Series { get; set; }
        }

        private class CskModContext
        {
            public IMusicMod Mod { get; set; }
            public string MetadataPath { get; set; }
            public JObject Metadata { get; set; }
            public string PackName { get; set; }
            public string SafePackName { get; set; }
            public List<JObject> SeriesList { get; set; }
            public Dictionary<string, string> SeriesIdToName { get; set; }
        }

        private class CskBuildResources
        {
            public List<CoreBgmRow> CoreBgmRows { get; set; }
            public Dictionary<string, string> VanillaGames { get; set; }
            public Dictionary<string, string> ToneToSeriesMap { get; set; }
            public HashSet<string> CoreToneIds { get; set; }
            public JObject PlaylistOverride { get; set; }
            public JObject OrderOverride { get; set; }
            public JObject CoreBgmOverride { get; set; }
            public JObject CoreGameOverride { get; set; }
            public JObject CoreSeriesOverride { get; set; }
            public JObject StageOverride { get; set; }
        }

        private class BgmBuildEntry
        {
            public string NameId { get; set; }
            public float AudioVolume { get; set; }
            public string Filename { get; set; }
        }

        private static readonly HashSet<string> DlcSeries = new HashSet<string>(new[]
        {
            "persona", "dragonquest", "banjokazooie", "fatalfury", "arms", "minecraft", "tekken", "kingdomhearts"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly HashSet<string> VanillaSeries = new HashSet<string>(new[]
        {
            "none", "mario", "mariokart", "wreckingcrew", "donkeykong", "zelda",
            "metroid", "yoshi", "kirby", "starfox", "pokemon", "fzero", "mother",
            "fireemblem", "gamewatch", "palutena", "wario", "pikmin", "famicomrobot",
            "doubutsu", "wiifit", "punchout", "xenoblade", "metalgear", "sonic",
            "rockman", "pacman", "streetfighter", "finalfantasy", "bayonetta",
            "splatoon", "castlevania", "smashbros", "arms", "persona",
            "dragonquest", "banjokazooie", "fatalfury", "minecraft",
            "tekken", "kingdomhearts", "etc"
        }, StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, List<string>> SeriesToPlaylist = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["doubutsu"] = new List<string> { "bgmanimal" },
            ["bayonetta"] = new List<string> { "bgmbeyo" },
            ["dragonquest"] = new List<string> { "bgmbrave" },
            ["banjokazooie"] = new List<string> { "bgmbuddy" },
            ["tekken"] = new List<string> { "bgmdemon" },
            ["donkeykong"] = new List<string> { "bgmdk" },
            ["fatalfury"] = new List<string> { "bgmdolly" },
            ["castlevania"] = new List<string> { "bgmdracula" },
            ["finalfantasy"] = new List<string> { "bgmedge", "bgmff" },
            ["xenoblade"] = new List<string> { "bgmelement", "bgmxenoblade" },
            ["fireemblem"] = new List<string> { "bgmfe", "bgmmaster" },
            ["starfox"] = new List<string> { "bgmfox" },
            ["fzero"] = new List<string> { "bgmfzero" },
            ["gamewatch"] = new List<string> { "bgmgamewatch" },
            ["palutena"] = new List<string> { "bgmicaros" },
            ["persona"] = new List<string> { "bgmjack" },
            ["kirby"] = new List<string> { "bgmkirby" },
            ["mario"] = new List<string> { "bgmmario" },
            ["metalgear"] = new List<string> { "bgmmetalgear" },
            ["metroid"] = new List<string> { "bgmmetroid" },
            ["mariokart"] = new List<string> { "bgmmkart" },
            ["mother"] = new List<string> { "bgmmother" },
            ["etc"] = new List<string> { "bgmother" },
            ["pacman"] = new List<string> { "bgmpacman" },
            ["minecraft"] = new List<string> { "bgmpickel" },
            ["pikmin"] = new List<string> { "bgmpikmin" },
            ["pokemon"] = new List<string> { "bgmpokemon" },
            ["punchout"] = new List<string> { "bgmpunchout" },
            ["rockman"] = new List<string> { "bgmrockman" },
            ["streetfighter"] = new List<string> { "bgmsf" },
            ["smashbros"] = new List<string> { "bgmsmashbtl" },
            ["sonic"] = new List<string> { "bgmsonic" },
            ["splatoon"] = new List<string> { "bgmspla" },
            ["arms"] = new List<string> { "bgmtantan" },
            ["kingdomhearts"] = new List<string> { "bgmtrail" },
            ["wario"] = new List<string> { "bgmwario" },
            ["wiifit"] = new List<string> { "bgmwiifit" },
            ["yoshi"] = new List<string> { "bgmyoshi" },
            ["zelda"] = new List<string> { "bgmzelda" }
        };

    }
}
