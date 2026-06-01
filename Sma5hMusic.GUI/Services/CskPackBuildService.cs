using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
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
        private const string CskTempFolder = "_csk_temp";
        private const string CloneBgmId = "ui_bgm_a29_ppm_medley";
        private const string CloneSeriesId = "ui_series_mario";
        private const string CloneGameTitleId = "ui_gametitle_paper_mario_series";
        private const string SmashBattlePlaylistId = "bgmsmashbtl";
        private const string SinglePackFolderName = "CSK Music Pack";

        private readonly IOptionsMonitor<ApplicationSettings> _config;
        private readonly IMusicModManagerService _musicModManagerService;
        private readonly INus3AudioService _nus3AudioService;
        private readonly IAudioStateService _audioStateService;
        private readonly ILogger _logger;

        public CskPackBuildService(
            IOptionsMonitor<ApplicationSettings> config,
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
            var overridePath = _config.CurrentValue.Sma5hMusicOverride.ModPath;
            var orderOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_ORDER_JSON_FILE));
            var playlistOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_PLAYLIST_JSON_FILE));
            var coreBgmOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_CORE_BGM_JSON_FILE));
            var coreGameOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_CORE_GAME_JSON_FILE));
            var coreSeriesOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_CORE_SERIES_JSON_FILE));
            var stageOverride = LoadJsonObject(Path.Combine(overridePath, MusicConstants.MusicModFiles.MUSIC_OVERRIDE_STAGE_JSON_FILE));
            var effectiveCoreGameOverride = BuildEffectiveCoreGameData(coreGameOverride);

            return new CskBuildResources
            {
                CoreGameSeriesById = BuildCoreGameSeriesById(effectiveCoreGameOverride),
                CoreBgmIds = BuildCoreBgmIds(),
                OrderOverride = BuildEffectiveOrderData(orderOverride),
                PlaylistData = BuildEffectivePlaylistData(playlistOverride),
                CoreBgmOverride = BuildEffectiveCoreBgmOverrideData(coreBgmOverride),
                CoreGameOverride = effectiveCoreGameOverride,
                CoreSeriesOverride = BuildEffectiveCoreSeriesData(coreSeriesOverride),
                StageOverride = BuildEffectiveStageData(stageOverride)
            };
        }

        private JObject BuildEffectiveOrderData(JObject orderOverride)
        {
            var orderData = new JObject();

            foreach (var bgmEntry in _audioStateService.GetBgmDbRootEntries().Where(p => !string.IsNullOrEmpty(p.UiBgmId)))
                orderData[bgmEntry.UiBgmId] = bgmEntry.TestDispOrder;

            OverlayProperties(orderData, orderOverride);
            return orderData;
        }

        private Dictionary<string, string> BuildCoreGameSeriesById(JObject effectiveCoreGameData)
        {
            var seriesNames = _audioStateService.GetSeriesEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiSeriesId))
                .GroupBy(p => p.UiSeriesId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First().NameId, StringComparer.OrdinalIgnoreCase);

            return _audioStateService.GetGameTitleEntries()
                .Where(p => p.Source == EntrySource.Core && !string.IsNullOrEmpty(p.UiGameTitleId))
                .GroupBy(p => p.UiGameTitleId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    p => p.Key,
                    p =>
                    {
                        var game = effectiveCoreGameData[p.Key] as JObject;
                        var uiSeriesId = GetString(game, "ui_series_id", p.First().UiSeriesId);
                        return GetSeriesNameFromUiSeriesId(uiSeriesId, seriesNames);
                    },
                    StringComparer.OrdinalIgnoreCase);
        }

        private HashSet<string> BuildCoreBgmIds()
        {
            return _audioStateService.GetBgmDbRootEntries()
                .Where(p => p.Source == EntrySource.Core && !string.IsNullOrEmpty(p.UiBgmId))
                .Select(p => p.UiBgmId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        private JObject BuildEffectivePlaylistData(JObject playlistOverride)
        {
            var playlistData = new JObject();

            foreach (var playlist in _audioStateService.GetPlaylists())
                playlistData[playlist.Id] = CreatePlaylistObject(playlist);

            if (playlistOverride == null)
                return playlistData;

            foreach (var playlistProperty in playlistOverride.Properties())
            {
                var overridePlaylist = playlistProperty.Value as JObject;
                if (overridePlaylist == null)
                    continue;

                playlistData[playlistProperty.Name] = NormalizePlaylistObject(playlistProperty.Name, overridePlaylist);
            }

            return playlistData;
        }

        private JObject BuildEffectiveCoreGameData(JObject coreGameOverride)
        {
            var gameData = new JObject();

            foreach (var game in _audioStateService.GetGameTitleEntries().Where(p => !string.IsNullOrEmpty(p.UiGameTitleId)))
                gameData[game.UiGameTitleId] = CreateGameObject(game);

            OverlayProperties(gameData, coreGameOverride);
            return gameData;
        }

        private JObject BuildEffectiveCoreSeriesData(JObject coreSeriesOverride)
        {
            var seriesData = new JObject();

            foreach (var series in _audioStateService.GetSeriesEntries().Where(p => !string.IsNullOrEmpty(p.UiSeriesId)))
                seriesData[series.UiSeriesId] = CreateSeriesObject(series);

            OverlayProperties(seriesData, coreSeriesOverride);
            return seriesData;
        }

        private JObject BuildEffectiveStageData(JObject stageOverride)
        {
            var stageData = new JObject();

            foreach (var stage in _audioStateService.GetStagesEntries().Where(p => !string.IsNullOrEmpty(p.UiStageId)))
                stageData[stage.UiStageId] = CreateStageObject(stage);

            OverlayProperties(stageData, stageOverride);
            return stageData;
        }

        private JObject BuildEffectiveCoreBgmOverrideData(JObject coreBgmOverride)
        {
            if (coreBgmOverride == null)
                return null;

            var output = (JObject)coreBgmOverride.DeepClone();
            var dbRoots = EnsureObject(output, "CoreBgmDbRootOverrides");
            var streamSets = EnsureObject(output, "CoreBgmStreamSetOverrides");
            var assignedInfos = EnsureObject(output, "CoreBgmAssignedInfoOverrides");
            var streamProperties = EnsureObject(output, "CoreBgmStreamPropertyOverrides");
            var bgmProperties = EnsureObject(output, "CoreBgmPropertyOverrides");

            var dbRootEntries = _audioStateService.GetBgmDbRootEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiBgmId))
                .ToDictionary(p => p.UiBgmId, p => p, StringComparer.OrdinalIgnoreCase);
            var streamSetEntries = _audioStateService.GetBgmStreamSetEntries()
                .Where(p => !string.IsNullOrEmpty(p.StreamSetId))
                .ToDictionary(p => p.StreamSetId, p => p, StringComparer.OrdinalIgnoreCase);
            var assignedInfoEntries = _audioStateService.GetBgmAssignedInfoEntries()
                .Where(p => !string.IsNullOrEmpty(p.InfoId))
                .ToDictionary(p => p.InfoId, p => p, StringComparer.OrdinalIgnoreCase);
            var streamPropertyEntries = _audioStateService.GetBgmStreamPropertyEntries()
                .Where(p => !string.IsNullOrEmpty(p.StreamId))
                .ToDictionary(p => p.StreamId, p => p, StringComparer.OrdinalIgnoreCase);
            var bgmPropertyEntries = _audioStateService.GetBgmPropertyEntries()
                .Where(p => !string.IsNullOrEmpty(p.NameId))
                .ToDictionary(p => p.NameId, p => p, StringComparer.OrdinalIgnoreCase);

            foreach (var dbProperty in dbRoots.Properties().ToList())
            {
                var uiBgmId = dbProperty.Name;
                if (dbRootEntries.ContainsKey(uiBgmId))
                    dbRoots[uiBgmId] = MergeObjects(CreateBgmDbRootObject(dbRootEntries[uiBgmId]), dbProperty.Value as JObject);

                var db = dbRoots[uiBgmId] as JObject;
                var streamSetId = GetString(db, "stream_set_id");
                if (string.IsNullOrEmpty(streamSetId))
                    continue;

                if (streamSetEntries.ContainsKey(streamSetId))
                    streamSets[streamSetId] = MergeObjects(CreateStreamSetObject(streamSetEntries[streamSetId]), streamSets[streamSetId] as JObject);

                var streamSet = streamSets[streamSetId] as JObject;
                for (var i = 0; i < 16; i++)
                {
                    var infoId = GetString(streamSet, $"info{i}");
                    if (string.IsNullOrEmpty(infoId))
                        continue;

                    if (assignedInfoEntries.ContainsKey(infoId))
                        assignedInfos[infoId] = MergeObjects(CreateAssignedInfoObject(assignedInfoEntries[infoId]), assignedInfos[infoId] as JObject);

                    var assigned = assignedInfos[infoId] as JObject;
                    var streamId = GetString(assigned, "stream_id");
                    if (string.IsNullOrEmpty(streamId))
                        continue;

                    if (streamPropertyEntries.ContainsKey(streamId))
                        streamProperties[streamId] = MergeObjects(CreateStreamPropertyObject(streamPropertyEntries[streamId]), streamProperties[streamId] as JObject);

                    var streamProperty = streamProperties[streamId] as JObject;
                    var nameId = GetString(streamProperty, "data_name0");
                    if (!string.IsNullOrEmpty(nameId) && bgmPropertyEntries.ContainsKey(nameId))
                        bgmProperties[nameId] = MergeObjects(CreateBgmPropertyObject(bgmPropertyEntries[nameId]), bgmProperties[nameId] as JObject);
                }
            }

            return output;
        }

        private static JObject CreatePlaylistObject(PlaylistEntry playlist)
        {
            return new JObject
            {
                ["id"] = playlist.Id,
                ["title"] = playlist.Title,
                ["tracks"] = new JArray(playlist.Tracks.Select(CreatePlaylistTrack))
            };
        }

        private static JObject CreatePlaylistTrack(PlaylistValueEntry track)
        {
            var orders = new[]
            {
                track.Order0, track.Order1, track.Order2, track.Order3,
                track.Order4, track.Order5, track.Order6, track.Order7,
                track.Order8, track.Order9, track.Order10, track.Order11,
                track.Order12, track.Order13, track.Order14, track.Order15
            };
            var incidences = new[]
            {
                track.Incidence0, track.Incidence1, track.Incidence2, track.Incidence3,
                track.Incidence4, track.Incidence5, track.Incidence6, track.Incidence7,
                track.Incidence8, track.Incidence9, track.Incidence10, track.Incidence11,
                track.Incidence12, track.Incidence13, track.Incidence14, track.Incidence15
            };

            var output = new JObject { ["ui_bgm_id"] = track.UiBgmId };
            for (var i = 0; i < 16; i++)
            {
                output[$"o{i}"] = orders[i];
                output[$"i{i}"] = incidences[i];
            }

            return output;
        }

        private static JObject NormalizePlaylistObject(string playlistId, JObject playlist)
        {
            return new JObject
            {
                ["id"] = GetString(playlist, "id", playlistId),
                ["title"] = GetString(playlist, "title"),
                ["tracks"] = new JArray(GetArray(playlist, "tracks").OfType<JObject>().Select(NormalizePlaylistTrack))
            };
        }

        private static JObject NormalizePlaylistTrack(JObject track)
        {
            var output = new JObject { ["ui_bgm_id"] = GetString(track, "ui_bgm_id") };
            for (var i = 0; i < 16; i++)
            {
                output[$"o{i}"] = GetInt(track, $"o{i}", GetInt(track, $"order{i}", 0));
                output[$"i{i}"] = GetInt(track, $"i{i}", GetInt(track, $"incidence{i}", 10000));
            }

            return output;
        }

        private static JObject CreateGameObject(GameTitleEntry game)
        {
            return new JObject
            {
                ["ui_gametitle_id"] = game.UiGameTitleId,
                ["name_id"] = game.NameId,
                ["ui_series_id"] = game.UiSeriesId,
                ["0x1c38302364"] = game.Unk1,
                ["release"] = game.Release,
                ["msbt_title"] = CreateLocalizedObject(game.MSBTTitle)
            };
        }

        private static JObject CreateSeriesObject(SeriesEntry series)
        {
            return new JObject
            {
                ["ui_series_id"] = series.UiSeriesId,
                ["name_id"] = series.NameId,
                ["disp_order"] = 0,
                ["disp_order_sound"] = series.DispOrderSound,
                ["save_no"] = 0,
                ["0x1c38302364"] = series.Unk1,
                ["is_dlc"] = series.IsDlc,
                ["is_patch"] = series.IsPatch,
                ["dlc_chara_id"] = series.DlcCharaId,
                ["is_use_amiibo_bg"] = false,
                ["msbt_title"] = CreateLocalizedObject(series.MSBTTitle)
            };
        }

        private static JObject CreateStageObject(StageEntry stage)
        {
            return new JObject
            {
                ["ui_stage_id"] = stage.UiStageId,
                ["name_id"] = stage.NameId,
                ["save_no"] = stage.SaveNo,
                ["ui_series_id"] = stage.UiSeriesId,
                ["can_select"] = stage.CanSelect,
                ["disp_order"] = stage.DispOrder,
                ["stage_place_id"] = stage.StagePlaceId,
                ["secret_stage_place_id"] = stage.SecretStagePlaceId,
                ["can_demo"] = stage.CanDemo,
                ["0x10359e17b0"] = stage.Unk1,
                ["is_usable_flag"] = stage.IsUsableFlag,
                ["is_usable_amiibo"] = stage.IsUsableAmiibo,
                ["secret_command_id"] = stage.SecretCommandId,
                ["secret_command_id_joycon"] = stage.SecretCommandIdJoycon,
                ["bgm_set_id"] = stage.BgmSetId,
                ["bgm_setting_no"] = stage.BgmSettingNo,
                ["bgm_selector"] = stage.BgmSelector,
                ["is_dlc"] = stage.IsDlc,
                ["is_patch"] = stage.IsPatch,
                ["dlc_chara_id"] = stage.DlcCharaId
            };
        }

        private static JObject CreateBgmDbRootObject(BgmDbRootEntry db)
        {
            return new JObject
            {
                ["ui_bgm_id"] = db.UiBgmId,
                ["stream_set_id"] = db.StreamSetId,
                ["record_type"] = db.RecordType,
                ["ui_gametitle_id"] = db.UiGameTitleId,
                ["name_id"] = db.NameId,
                ["test_disp_order"] = db.TestDispOrder,
                ["msbt_title"] = CreateLocalizedObject(db.Title),
                ["msbt_author"] = CreateLocalizedObject(db.Author),
                ["msbt_copyright"] = CreateLocalizedObject(db.Copyright)
            };
        }

        private static JObject CreateStreamSetObject(BgmStreamSetEntry streamSet)
        {
            var output = new JObject
            {
                ["stream_set_id"] = streamSet.StreamSetId,
                ["special_category"] = streamSet.SpecialCategory
            };

            var infos = new[]
            {
                streamSet.Info0, streamSet.Info1, streamSet.Info2, streamSet.Info3,
                streamSet.Info4, streamSet.Info5, streamSet.Info6, streamSet.Info7,
                streamSet.Info8, streamSet.Info9, streamSet.Info10, streamSet.Info11,
                streamSet.Info12, streamSet.Info13, streamSet.Info14, streamSet.Info15
            };

            for (var i = 0; i < infos.Length; i++)
                output[$"info{i}"] = infos[i];

            return output;
        }

        private static JObject CreateAssignedInfoObject(BgmAssignedInfoEntry assignedInfo)
        {
            return new JObject
            {
                ["info_id"] = assignedInfo.InfoId,
                ["stream_id"] = assignedInfo.StreamId,
                ["condition"] = assignedInfo.Condition,
                ["condition_process"] = assignedInfo.ConditionProcess,
                ["start_frame"] = assignedInfo.StartFrame,
                ["change_fadein_frame"] = assignedInfo.ChangeFadeInFrame,
                ["change_start_delay_frame"] = assignedInfo.ChangeStartDelayFrame,
                ["change_fadeout_frame"] = assignedInfo.ChangeFadoutFrame,
                ["change_stop_delay_frame"] = assignedInfo.ChangeStopDelayFrame,
                ["menu_change_fadein_frame"] = assignedInfo.MenuChangeFadeInFrame,
                ["menu_change_start_delay_frame"] = assignedInfo.MenuChangeStartDelayFrame,
                ["menu_change_fadeout_frame"] = assignedInfo.MenuChangeFadeOutFrame,
                ["menu_change_stop_delay_frame"] = assignedInfo.MenuChangeStopDelayFrame
            };
        }

        private static JObject CreateStreamPropertyObject(BgmStreamPropertyEntry streamProperty)
        {
            return new JObject
            {
                ["stream_id"] = streamProperty.StreamId,
                ["data_name0"] = streamProperty.DataName0,
                ["data_name1"] = streamProperty.DataName1,
                ["data_name2"] = streamProperty.DataName2,
                ["data_name3"] = streamProperty.DataName3,
                ["data_name4"] = streamProperty.DataName4,
                ["loop"] = streamProperty.Loop,
                ["end_point"] = streamProperty.EndPoint,
                ["fadeout_frame"] = streamProperty.FadeOutFrame,
                ["start_point_suddendeath"] = streamProperty.StartPointSuddenDeath,
                ["start_point_transition"] = streamProperty.StartPointTransition,
                ["start_point0"] = streamProperty.StartPoint0,
                ["start_point1"] = streamProperty.StartPoint1,
                ["start_point2"] = streamProperty.StartPoint2,
                ["start_point3"] = streamProperty.StartPoint3,
                ["start_point4"] = streamProperty.StartPoint4
            };
        }

        private static JObject CreateBgmPropertyObject(BgmPropertyEntry bgmProperty)
        {
            return new JObject
            {
                ["name_id"] = bgmProperty.NameId,
                ["loop_start_ms"] = bgmProperty.LoopStartMs,
                ["loop_start_sample"] = bgmProperty.LoopStartSample,
                ["loop_end_ms"] = bgmProperty.LoopEndMs,
                ["loop_end_sample"] = bgmProperty.LoopEndSample,
                ["total_time_ms"] = bgmProperty.TotalTimeMs,
                ["total_samples"] = bgmProperty.TotalSamples
            };
        }

        private static JObject CreateLocalizedObject(Dictionary<string, string> localizedText)
        {
            var output = new JObject();
            if (localizedText == null)
                return output;

            foreach (var entry in localizedText)
                output[entry.Key] = entry.Value;

            return output;
        }

        private static JObject EnsureObject(JObject parent, string key)
        {
            var value = parent[key] as JObject;
            if (value != null)
                return value;

            value = new JObject();
            parent[key] = value;
            return value;
        }

        private static JObject MergeObjects(JObject baseObject, JObject overrideObject)
        {
            var output = baseObject != null ? (JObject)baseObject.DeepClone() : new JObject();
            OverlayProperties(output, overrideObject);
            return output;
        }

        private static void OverlayProperties(JObject target, JObject source)
        {
            if (target == null || source == null)
                return;

            foreach (var property in source.Properties())
                target[property.Name] = property.Value.DeepClone();
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
            var allSeries = contexts.SelectMany(context => context.SeriesList).ToList();
            var etcSelected = contexts.Any(context => context.SeriesList
                .Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series)))
                .Any(IsEtcSeries));
            var seriesSoundOrder = BuildSeriesSoundOrder(
                allSeries,
                buildResources.OrderOverride,
                etcSelected);

            foreach (var context in contexts)
            {
                _logger.LogInformation("Generating CSK packs from {MetadataPath}", context.MetadataPath);

                foreach (var series in context.SeriesList.Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series))))
                {
                    var savedPath = ProcessSeries(
                        series,
                        context.SafePackName,
                        outputRoot,
                        generatedBgmFolder,
                        buildResources.PlaylistData,
                        context.SeriesIdToName,
                        buildResources.CoreBgmOverride,
                        buildResources.OrderOverride,
                        buildResources.CoreGameSeriesById,
                        seriesSoundOrder,
                        buildResources.StageOverride,
                        buildResources.CoreGameOverride,
                        buildResources.CoreSeriesOverride,
                        context.Metadata,
                        buildResources.CoreBgmIds);

                    _logger.LogInformation("[CSK] Saved {SeriesName}: {SavedPath}", GetString(series, "name_id", "<unknown>"), savedPath);
                }
            }
        }

        private void GenerateSingleCskPack(IEnumerable<CskModContext> contexts, string generatedBgmFolder, string outputRoot, HashSet<string> selectedSeriesKeys, CskBuildResources buildResources)
        {
            var contextList = contexts.ToList();
            var allSeries = contextList.SelectMany(context => context.SeriesList).ToList();
            var selectedSeries = contextList
                .SelectMany(context => context.SeriesList
                    .Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series)))
                    .Select(series => new { Context = context, Series = series }))
                .ToList();

            if (selectedSeries.Count == 0)
                throw new InvalidOperationException("No selected series were found in the currently loaded music mods.");

            var etcSelected = selectedSeries.Any(p => IsEtcSeries(p.Series));
            var seriesSoundOrder = BuildSeriesSoundOrder(allSeries, buildResources.OrderOverride, etcSelected);
            var seriesIdToName = contextList
                .SelectMany(context => context.SeriesIdToName)
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First().Value, StringComparer.OrdinalIgnoreCase);

            var packRoot = Path.Combine(outputRoot, SinglePackFolderName);
            var databaseFolder = Path.Combine(packRoot, "database");
            var uiFolder = Path.Combine(packRoot, "ui", "message");
            Directory.CreateDirectory(databaseFolder);
            Directory.CreateDirectory(uiFolder);

            var songData = CreateSongData();
            var msgBgmEntries = new List<string>();
            var msgTitleEntries = new List<string>();

            foreach (var item in selectedSeries)
            {
                var seriesName = GetString(item.Series, "name_id", "<unknown>");
                _logger.LogInformation("[CSK] Adding {SeriesName} to single CSK pack.", seriesName);
                CopySeriesIcon(item.Series, packRoot);
                PopulateSeriesPackData(
                    item.Series,
                    songData,
                    msgBgmEntries,
                    msgTitleEntries,
                    SinglePackFolderName,
                    outputRoot,
                    generatedBgmFolder,
                    buildResources.PlaylistData,
                    seriesIdToName,
                    buildResources.CoreBgmOverride,
                    buildResources.OrderOverride,
                    buildResources.CoreGameSeriesById,
                    seriesSoundOrder,
                    buildResources.StageOverride,
                    buildResources.CoreGameOverride,
                    buildResources.CoreSeriesOverride,
                    item.Context.Metadata,
                    buildResources.CoreBgmIds);
            }

            NormalizeCombinedSongData(songData);
            WriteCombinedXmsbt(Path.Combine(uiFolder, "msg_bgm.xmsbt"), msgBgmEntries);
            WriteCombinedXmsbt(Path.Combine(uiFolder, "msg_title.xmsbt"), msgTitleEntries);

            var outputJsonPath = Path.Combine(databaseFolder, "song_data.json");
            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(songData, Formatting.Indented), new UTF8Encoding(false));
            _logger.LogInformation("[CSK] Saved single CSK pack: {SavedPath}", outputJsonPath);
        }

        private Dictionary<string, int> BuildSeriesSoundOrder(IEnumerable<JObject> seriesList, JObject orderOverride, bool etcSelected)
        {
            var allSeries = seriesList.ToList();
            var seriesOrder = BuildSeriesSoundOrderFromAudioState(orderOverride);
            var metadataOrder = BuildSeriesSoundOrderFromMetadata(allSeries, orderOverride);

            foreach (var series in allSeries)
            {
                var uiSeriesId = GetString(series, "ui_series_id");
                var nameId = GetString(series, "name_id");

                if (!string.IsNullOrEmpty(uiSeriesId) && seriesOrder.ContainsKey(uiSeriesId))
                    SetSeriesOrderKey(seriesOrder, nameId, seriesOrder[uiSeriesId]);
                else if (!string.IsNullOrEmpty(nameId) && seriesOrder.ContainsKey(nameId))
                    SetSeriesOrderKey(seriesOrder, uiSeriesId, seriesOrder[nameId]);
            }

            foreach (var fallbackOrder in metadataOrder)
                SetSeriesOrderKey(seriesOrder, fallbackOrder.Key, fallbackOrder.Value);

            if (!etcSelected)
                ApplyEtcSoundOrderAnchor(allSeries, orderOverride, seriesOrder);

            return seriesOrder;
        }

        private Dictionary<string, int> BuildSeriesSoundOrderFromAudioState(JObject orderOverride)
        {
            var output = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var seriesEntries = _audioStateService.GetSeriesEntries()
                .Where(p => p.DispOrderSound > -1 && !string.IsNullOrEmpty(p.UiSeriesId))
                .GroupBy(p => p.UiSeriesId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First(), StringComparer.OrdinalIgnoreCase);
            var gameEntries = _audioStateService.GetGameTitleEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiGameTitleId) && !string.IsNullOrEmpty(p.UiSeriesId))
                .GroupBy(p => p.UiGameTitleId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First(), StringComparer.OrdinalIgnoreCase);
            var sortedGames = _audioStateService.GetBgmDbRootEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiGameTitleId))
                .Select(p => new
                {
                    p.UiGameTitleId,
                    Order = GetInt(orderOverride, p.UiBgmId, p.TestDispOrder)
                })
                .Where(p => p.Order >= 0)
                .OrderBy(p => p.Order)
                .GroupBy(p => p.UiGameTitleId, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.First().UiGameTitleId)
                .ToList();

            var index = 1;
            foreach (var gameId in sortedGames)
            {
                if (!gameEntries.ContainsKey(gameId))
                    continue;

                var uiSeriesId = gameEntries[gameId].UiSeriesId;
                if (!seriesEntries.ContainsKey(uiSeriesId) || output.ContainsKey(uiSeriesId))
                    continue;

                SetSeriesOrderKey(output, uiSeriesId, index);
                SetSeriesOrderKey(output, seriesEntries[uiSeriesId].NameId, index);
                if (index != sbyte.MaxValue)
                    index++;
            }

            return output;
        }

        private Dictionary<string, int> BuildSeriesSoundOrderFromMetadata(List<JObject> allSeries, JObject orderOverride)
        {
            if (orderOverride != null && orderOverride.HasValues)
            {
                var seriesMinOverride = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                var aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (var series in allSeries)
                {
                    var uiSeriesId = GetString(series, "ui_series_id");
                    var nameId = GetString(series, "name_id");
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

                    foreach (var uiBgmId in bgmIdsToCheck)
                    {
                        var value = GetInt(orderOverride, uiBgmId, int.MinValue);
                        if (value == int.MinValue || value == -1)
                            continue;

                        if (!minValue.HasValue || value < minValue.Value)
                            minValue = value;
                    }

                    SetMinSeriesOrder(seriesMinOverride, nameId, minValue ?? int.MaxValue);
                    if (!string.IsNullOrEmpty(uiSeriesId) && !string.IsNullOrEmpty(nameId))
                        aliases[uiSeriesId] = nameId;
                }

                var ranked = seriesMinOverride
                    .OrderBy(p => p.Value)
                    .ThenBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                    .Select((p, i) => new { p.Key, Index = i + 1 })
                    .ToDictionary(p => p.Key, p => p.Index, StringComparer.OrdinalIgnoreCase);

                foreach (var alias in aliases)
                {
                    if (ranked.ContainsKey(alias.Value))
                        ranked[alias.Key] = ranked[alias.Value];
                }

                return ranked;
            }

            return allSeries
                .Where(p => !VanillaSeries.Contains(GetString(p, "name_id")))
                .OrderBy(p => GetSeriesDisplayName(p).ToLowerInvariant())
                .Select((p, i) => new
                {
                    NameId = GetString(p, "name_id"),
                    UiSeriesId = GetString(p, "ui_series_id"),
                    Order = 39 + i
                })
                .SelectMany(p => new[]
                {
                    new { Key = p.NameId, p.Order },
                    new { Key = p.UiSeriesId, p.Order }
                })
                .Where(p => !string.IsNullOrEmpty(p.Key))
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First().Order, StringComparer.OrdinalIgnoreCase);
        }

        private void ApplyEtcSoundOrderAnchor(List<JObject> allSeries, JObject orderOverride, Dictionary<string, int> seriesOrder)
        {
            var etcSoundOrder = GetEtcDispOrderSound();
            var etcMinTestOrder = GetEtcMinTestDispOrder(orderOverride);
            if (!etcSoundOrder.HasValue || !etcMinTestOrder.HasValue)
                return;

            var afterEtcSeries = allSeries
                .Where(p => !IsEtcSeries(p) && !VanillaSeries.Contains(GetString(p, "name_id")))
                .Select(p => new
                {
                    Series = p,
                    Key = GetSeriesIdentity(p),
                    MinTestOrder = GetSeriesMinTestDispOrder(p, orderOverride)
                })
                .Where(p => !string.IsNullOrEmpty(p.Key) &&
                            p.MinTestOrder.HasValue &&
                            p.MinTestOrder.Value > etcMinTestOrder.Value)
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .Select(p => p.OrderBy(x => x.MinTestOrder.Value).First())
                .OrderBy(p => p.MinTestOrder.Value)
                .ThenBy(p => GetSeriesDisplayName(p.Series), StringComparer.OrdinalIgnoreCase)
                .ToList();

            var nextOrder = etcSoundOrder.Value + 1;
            foreach (var series in afterEtcSeries)
            {
                SetSeriesOrderKeyOverride(seriesOrder, GetString(series.Series, "name_id"), nextOrder);
                SetSeriesOrderKeyOverride(seriesOrder, GetString(series.Series, "ui_series_id"), nextOrder);

                if (nextOrder < sbyte.MaxValue)
                    nextOrder++;
            }
        }

        private int? GetEtcDispOrderSound()
        {
            return _audioStateService.GetSeriesEntries()
                .Where(p => p.DispOrderSound > -1 && IsEtcSeries(p.NameId, p.UiSeriesId))
                .OrderBy(p => p.Source == EntrySource.Core ? 0 : 1)
                .Select(p => (int?)p.DispOrderSound)
                .FirstOrDefault();
        }

        private int? GetEtcMinTestDispOrder(JObject orderOverride)
        {
            var etcSeriesIds = _audioStateService.GetSeriesEntries()
                .Where(p => IsEtcSeries(p.NameId, p.UiSeriesId))
                .Select(p => p.UiSeriesId)
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            etcSeriesIds.Add("ui_series_etc");

            var gameSeriesById = _audioStateService.GetGameTitleEntries()
                .Where(p => !string.IsNullOrEmpty(p.UiGameTitleId) && !string.IsNullOrEmpty(p.UiSeriesId))
                .GroupBy(p => p.UiGameTitleId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First().UiSeriesId, StringComparer.OrdinalIgnoreCase);

            int? minOrder = null;
            foreach (var bgm in _audioStateService.GetBgmDbRootEntries())
            {
                if (string.IsNullOrEmpty(bgm.UiGameTitleId) ||
                    !gameSeriesById.TryGetValue(bgm.UiGameTitleId, out var uiSeriesId) ||
                    !etcSeriesIds.Contains(uiSeriesId))
                    continue;

                var order = GetInt(orderOverride, bgm.UiBgmId, bgm.TestDispOrder);
                if (order < 0)
                    continue;

                if (!minOrder.HasValue || order < minOrder.Value)
                    minOrder = order;
            }

            return minOrder;
        }

        private static int? GetSeriesMinTestDispOrder(JObject series, JObject orderOverride)
        {
            int? minOrder = null;
            foreach (JObject game in GetArray(series, "games"))
            {
                foreach (JObject bgm in GetArray(game, "bgms"))
                {
                    var dbRoot = bgm["db_root"] as JObject;
                    var uiBgmId = GetString(dbRoot, "ui_bgm_id");
                    if (string.IsNullOrEmpty(uiBgmId))
                        continue;

                    var order = GetInt(orderOverride, uiBgmId, GetInt(dbRoot, "test_disp_order", -1));
                    if (order < 0)
                        continue;

                    if (!minOrder.HasValue || order < minOrder.Value)
                        minOrder = order;
                }
            }

            return minOrder;
        }

        private static string GetSeriesIdentity(JObject series)
        {
            var uiSeriesId = GetString(series, "ui_series_id");
            return !string.IsNullOrEmpty(uiSeriesId) ? uiSeriesId : GetString(series, "name_id");
        }

        private static bool IsEtcSeries(JObject series)
        {
            return IsEtcSeries(GetString(series, "name_id"), GetString(series, "ui_series_id"));
        }

        private static bool IsEtcSeries(string nameId, string uiSeriesId)
        {
            return string.Equals(uiSeriesId, "ui_series_etc", StringComparison.OrdinalIgnoreCase) ||
                   (!string.IsNullOrEmpty(nameId) && nameId.StartsWith("etc", StringComparison.OrdinalIgnoreCase));
        }

        private static int GetSeriesSoundOrder(Dictionary<string, int> seriesSoundOrder, JObject series)
        {
            var uiSeriesId = GetString(series, "ui_series_id");
            if (!string.IsNullOrEmpty(uiSeriesId) && seriesSoundOrder.ContainsKey(uiSeriesId))
                return seriesSoundOrder[uiSeriesId];

            var nameId = GetString(series, "name_id");
            if (!string.IsNullOrEmpty(nameId) && seriesSoundOrder.ContainsKey(nameId))
                return seriesSoundOrder[nameId];

            return 1;
        }

        private static void SetSeriesOrderKey(Dictionary<string, int> seriesOrder, string key, int value)
        {
            if (string.IsNullOrEmpty(key) || seriesOrder.ContainsKey(key))
                return;

            seriesOrder[key] = value;
        }

        private static void SetSeriesOrderKeyOverride(Dictionary<string, int> seriesOrder, string key, int value)
        {
            if (string.IsNullOrEmpty(key))
                return;

            seriesOrder[key] = value;
        }

        private static void SetMinSeriesOrder(Dictionary<string, int> seriesOrder, string seriesName, int value)
        {
            if (string.IsNullOrEmpty(seriesName))
                return;

            if (!seriesOrder.TryGetValue(seriesName, out var currentValue) || value < currentValue)
                seriesOrder[seriesName] = value;
        }

        private static JObject CreateSeriesDatabaseEntry(JObject series, JObject coreSeriesOverride, int dispOrderSound)
        {
            var uiSeriesId = GetString(series, "ui_series_id");
            var seriesName = GetString(series, "name_id");
            var effectiveSeries = GetEffectiveOverrideObject(series, coreSeriesOverride, "ui_series_id");
            var isDlcSeries = DlcSeries.Contains(seriesName);
            var entry = new JObject
            {
                ["ui_series_id"] = GetString(effectiveSeries, "ui_series_id", uiSeriesId),
                ["clone_from_series_id"] = CloneSeriesId,
                ["name_id"] = GetString(effectiveSeries, "name_id", seriesName),
                ["disp_order"] = 0,
                ["disp_order_sound"] = dispOrderSound,
                ["save_no"] = 0,
                ["shown_as_series_in_directory"] = false,
                ["is_dlc"] = GetBool(effectiveSeries, "is_dlc", isDlcSeries),
                ["is_patch"] = GetBool(effectiveSeries, "is_patch", isDlcSeries),
                ["is_use_amiibo_bg"] = false
            };

            var dlcCharaId = GetString(effectiveSeries, "dlc_chara_id");
            if (!string.IsNullOrEmpty(dlcCharaId))
                entry["dlc_chara_id"] = dlcCharaId;

            return entry;
        }

        private static JObject GetEffectiveOverrideObject(JObject source, JObject overrides, string idKey)
        {
            if (source == null)
                source = new JObject();

            if (overrides == null)
                return source;

            var id = GetString(source, idKey);
            var overrideObject = overrides[id] as JObject;
            return overrideObject == null ? source : MergeObjects(source, overrideObject);
        }

        private static bool ShouldAddGameTitleEntry(JObject game, string seriesName, Dictionary<string, string> coreGameSeriesById)
        {
            var uiGameTitleId = GetString(game, "ui_gametitle_id");
            if (string.IsNullOrEmpty(uiGameTitleId) || !coreGameSeriesById.ContainsKey(uiGameTitleId))
                return true;

            return !string.Equals(coreGameSeriesById[uiGameTitleId], seriesName, StringComparison.OrdinalIgnoreCase);
        }

        private string ProcessSeries(
            JObject series,
            string packName,
            string outputRoot,
            string generatedBgmFolder,
            JObject playlistData,
            Dictionary<string, string> seriesIdToName,
            JObject coreBgmOverride,
            JObject orderOverride,
            Dictionary<string, string> coreGameSeriesById,
            Dictionary<string, int> seriesSoundOrder,
            JObject stageOverride,
            JObject coreGameOverride,
            JObject coreSeriesOverride,
            JObject metadata,
            HashSet<string> coreBgmIds)
        {
            var seriesName = GetString(series, "name_id");
            var realName = GetSeriesDisplayName(series);
            var safeSeriesName = SanitizePathSegment(realName, seriesName, "series folder name");
            var seriesFolderName = SanitizePathSegment($"{packName} - {safeSeriesName}", seriesName, "full series folder name");

            var seriesDbFolder = Path.Combine(outputRoot, seriesFolderName, "database");
            var seriesUiFolder = Path.Combine(outputRoot, seriesFolderName, "ui", "message");
            Directory.CreateDirectory(seriesDbFolder);
            Directory.CreateDirectory(seriesUiFolder);
            CopySeriesIcon(series, Path.Combine(outputRoot, seriesFolderName));

            var songData = CreateSongData();
            var msgBgmEntries = new List<string>();
            var msgTitleEntries = new List<string>();

            PopulateSeriesPackData(
                series,
                songData,
                msgBgmEntries,
                msgTitleEntries,
                seriesFolderName,
                outputRoot,
                generatedBgmFolder,
                playlistData,
                seriesIdToName,
                coreBgmOverride,
                orderOverride,
                coreGameSeriesById,
                seriesSoundOrder,
                stageOverride,
                coreGameOverride,
                coreSeriesOverride,
                metadata,
                coreBgmIds);

            var outputJsonPath = Path.Combine(seriesDbFolder, $"{SanitizePathSegment(seriesName, "series", "series database file name")}.json");
            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(songData, Formatting.Indented), new UTF8Encoding(false));
            WriteXmsbt(Path.Combine(seriesUiFolder, "msg_bgm.xmsbt"), msgBgmEntries);
            WriteXmsbt(Path.Combine(seriesUiFolder, "msg_title.xmsbt"), msgTitleEntries);
            return outputJsonPath;
        }

        private void PopulateSeriesPackData(
            JObject series,
            JObject songData,
            List<string> msgBgmEntries,
            List<string> msgTitleEntries,
            string seriesFolderName,
            string outputRoot,
            string generatedBgmFolder,
            JObject playlistData,
            Dictionary<string, string> seriesIdToName,
            JObject coreBgmOverride,
            JObject orderOverride,
            Dictionary<string, string> coreGameSeriesById,
            Dictionary<string, int> seriesSoundOrder,
            JObject stageOverride,
            JObject coreGameOverride,
            JObject coreSeriesOverride,
            JObject metadata,
            HashSet<string> coreBgmIds)
        {
            var seriesName = GetString(series, "name_id");
            var orderCounter = GetNextPlaylistOrder(seriesName, playlistData);
            var seriesTitle = GetString(series["msbt_title"], "us_en", seriesName);

            if (coreSeriesOverride != null)
            {
                var overrideEntry = coreSeriesOverride[GetString(series, "ui_series_id")] as JObject;
                if (overrideEntry != null)
                    seriesTitle = GetString(overrideEntry["msbt_title"], "us_en", seriesTitle);
            }

            if (orderOverride != null || !VanillaSeries.Contains(seriesName) || seriesName.StartsWith("etc", StringComparison.OrdinalIgnoreCase))
            {
                var dispOrderSound = GetSeriesSoundOrder(seriesSoundOrder, series);
                if (dispOrderSound > 127)
                    dispOrderSound = 127;

                GetArray(songData, "series_database_entries").Add(CreateSeriesDatabaseEntry(series, coreSeriesOverride, dispOrderSound));
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

                var effectiveGame = GetEffectiveOverrideObject(game, coreGameOverride, "ui_gametitle_id");
                var gameName = GetString(effectiveGame, "name_id", GetString(game, "name_id"));
                if (ShouldAddGameTitleEntry(effectiveGame, seriesName, coreGameSeriesById))
                    AddGameTitleEntry(songData, effectiveGame);

                var gameTitle = GetString(effectiveGame["msbt_title"], "us_en", gameName);
                msgTitleEntries.Add(MakeEntry($"tit_{gameName}", EscapeXml(gameTitle)));

                foreach (JObject bgm in GetArray(game, "bgms"))
                    orderCounter = ProcessBgm(bgm, songData, playlistData, msgBgmEntries, coreBgmOverride, orderOverride, seriesName, seriesFolderName, outputRoot, generatedBgmFolder, orderCounter);
            }

            ProcessCoreGameMovedBgms(series, metadata, coreGameOverride, songData, playlistData, msgBgmEntries, msgTitleEntries, coreBgmOverride, orderOverride, seriesName, seriesFolderName, outputRoot, generatedBgmFolder, ref orderCounter);
            ProcessPlaylistsAndStages(songData, msgBgmEntries, seriesName, playlistData, stageOverride, coreBgmIds, coreBgmOverride, orderOverride);
            ProcessCoreBgmOverrides(songData, msgBgmEntries, msgTitleEntries, seriesName, seriesIdToName, coreBgmOverride, orderOverride, coreGameOverride);
            AddCoreGameOverridesForNewSeries(songData, series, seriesName, coreGameOverride);
        }

        private int ProcessBgm(
            JObject bgm,
            JObject songData,
            JObject playlistOverride,
            List<string> msgBgmEntries,
            JObject coreBgmOverride,
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
            var nameId = GetString(bgmProp, "name_id");
            var handledByCoreBgmOverride = IsCoreBgmOverride(coreBgmOverride, uiBgmId);
            var testDispOrder = orderOverride != null ? GetInt(orderOverride, uiBgmId, GetInt(db, "test_disp_order", 0)) : 0;

            // If this song is present in CoreBgmOverride, let ProcessCoreBgmOverrides add the
            // database/stream/message entries. ProcessBgm still handles playlists and file copy.
            if (!handledByCoreBgmOverride && !HasBgmDatabaseEntry(songData, uiBgmId))
            {
                GetArray(songData, "bgm_database_entries").Add(new JObject
                {
                    ["ui_bgm_id"] = uiBgmId,
                    ["clone_from_ui_bgm_id"] = CloneBgmId,
                    ["stream_set_id"] = GetString(db, "stream_set_id"),
                    ["name_id"] = nameId,
                    ["ui_gametitle_id"] = GetString(db, "ui_gametitle_id"),
                    ["test_disp_order"] = testDispOrder,
                    ["record_type"] = GetString(db, "record_type", "record_original")
                });

                AddUniqueJObjectByKey(songData, "stream_set_entries", "stream_set_id", CreateStreamSetEntry(streamSet));
                AddUniqueJObjectByKey(songData, "assigned_info_entries", "info_id", new JObject
                {
                    ["info_id"] = GetString(assigned, "info_id"),
                    ["stream_id"] = GetString(assigned, "stream_id"),
                    ["condition"] = GetString(assigned, "condition"),
                    ["condition_process"] = "sound_condition_process_add",
                    ["change_fadeout_frame"] = 60,
                    ["menu_change_fadeout_frame"] = 60
                });

                AddUniqueJObjectByKey(songData, "stream_property_entries", "stream_id", new JObject
                {
                    ["stream_id"] = GetString(streamProp, "stream_id"),
                    ["data_name0"] = GetString(streamProp, "data_name0")
                });

                AddUniqueJObjectByKey(songData, "bgm_property_entries", "stream_name", new JObject
                {
                    ["stream_name"] = GetString(streamProp, "data_name0"),
                    ["loop_start_ms"] = GetInt(bgmProp, "loop_start_ms", 0),
                    ["loop_start_sample"] = GetInt(bgmProp, "loop_start_sample", 0),
                    ["loop_end_ms"] = GetInt(bgmProp, "loop_end_ms", 0),
                    ["loop_end_sample"] = GetInt(bgmProp, "loop_end_sample", 0),
                    ["duration_ms"] = GetInt(bgmProp, "total_time_ms", 0),
                    ["duration_sample"] = GetInt(bgmProp, "total_samples", 0)
                });

                var titleText = GetString(db["msbt_title"], "us_en", nameId);
                AddUniqueMessage(msgBgmEntries, $"bgm_title_{nameId}", titleText);

                var authorText = GetString(db["msbt_author"], "us_en");
                AddUniqueMessage(msgBgmEntries, $"bgm_author_{nameId}", authorText);

                var copyrightText = GetString(db["msbt_copyright"], "us_en");
                AddUniqueMessage(msgBgmEntries, $"bgm_copyright_{nameId}", copyrightText);
            }

            orderCounter = AddToPlaylists(uiBgmId, songData, playlistOverride, seriesName, orderCounter);

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
            JObject coreBgmOverride,
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
                    orderCounter = ProcessBgm(bgm, songData, playlistOverride, msgBgmEntries, coreBgmOverride, orderOverride, seriesName, seriesFolderName, outputRoot, generatedBgmFolder, orderCounter);
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

        private static string GetCoreBgmSeriesName(
            string uiGameTitleId,
            string dbSeriesId,
            Dictionary<string, string> seriesIdToName,
            JObject coreGameOverride)
        {
            var gameOverride = coreGameOverride != null && !string.IsNullOrEmpty(uiGameTitleId)
                ? coreGameOverride[uiGameTitleId] as JObject
                : null;
            var gameSeriesId = GetString(gameOverride, "ui_series_id");
            var gameSeriesName = GetSeriesNameFromUiSeriesId(gameSeriesId, seriesIdToName);
            if (!string.IsNullOrEmpty(gameSeriesName))
                return gameSeriesName;

            return GetSeriesNameFromUiSeriesId(dbSeriesId, seriesIdToName);
        }

        private static string GetSeriesNameFromUiSeriesId(string uiSeriesId, Dictionary<string, string> seriesIdToName)
        {
            if (string.IsNullOrEmpty(uiSeriesId))
                return null;

            if (seriesIdToName.ContainsKey(uiSeriesId))
                return seriesIdToName[uiSeriesId];

            return uiSeriesId.StartsWith("ui_series_", StringComparison.OrdinalIgnoreCase)
                ? uiSeriesId.Substring("ui_series_".Length)
                : uiSeriesId;
        }

        private void ProcessCoreBgmOverrides(
            JObject songData,
            List<string> msgBgmEntries,
            List<string> msgTitleEntries,
            string seriesName,
            Dictionary<string, string> seriesIdToName,
            JObject coreBgmOverride,
            JObject orderOverride,
            JObject coreGameOverride)
        {
            if (coreBgmOverride == null)
                return;

            var alreadyAdded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dbRoots = coreBgmOverride["CoreBgmDbRootOverrides"] as JObject ?? new JObject();
            var streamSets = coreBgmOverride["CoreBgmStreamSetOverrides"] as JObject ?? new JObject();
            var assignedInfos = coreBgmOverride["CoreBgmAssignedInfoOverrides"] as JObject ?? new JObject();
            var addedAssignedInfos = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var dbProperty in dbRoots.Properties())
            {
                var uiBgmId = dbProperty.Name;
                var db = dbProperty.Value as JObject;
                var dbSeriesId = GetString(db, "ui_series_id");
                var uiGameTitleId = GetString(db, "ui_gametitle_id");
                var coreSeries = GetCoreBgmSeriesName(uiGameTitleId, dbSeriesId, seriesIdToName, coreGameOverride);

                if (coreSeries != seriesName)
                    continue;

                var streamSetId = GetString(db, "stream_set_id");
                var streamSetData = streamSets[streamSetId] as JObject ?? new JObject();
                var testDispOrder = orderOverride != null ? GetInt(orderOverride, uiBgmId, GetInt(db, "test_disp_order", 0)) : 0;
                var nameId = GetString(db, "name_id", uiBgmId);

                if (HasBgmDatabaseEntry(songData, uiBgmId))
                    continue;

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

                AddUniqueJObjectByKey(songData, "stream_set_entries", "stream_set_id", CreateStreamSetEntry(streamSetData, streamSetId));

                for (var i = 0; i < 16; i++)
                {
                    var infoKey = GetString(streamSetData, $"info{i}");
                    var assigned = assignedInfos[infoKey] as JObject;
                    if (assigned == null)
                        continue;

                    if (addedAssignedInfos.Add(infoKey))
                        AddUniqueJObjectByKey(songData, "assigned_info_entries", "info_id", CreateAssignedInfoEntry(assigned));
                }

                AddOptionalBgmMessageUnique(msgBgmEntries, $"bgm_title_{nameId}", db["msbt_title"]);
                AddOptionalBgmMessageUnique(msgBgmEntries, $"bgm_author_{nameId}", db["msbt_author"]);
                AddOptionalBgmMessageUnique(msgBgmEntries, $"bgm_copyright_{nameId}", db["msbt_copyright"]);

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

        private void ProcessPlaylistsAndStages(JObject songData, List<string> msgBgmEntries, string seriesName, JObject playlistData, JObject stageOverride, HashSet<string> coreBgmIds, JObject coreBgmOverride, JObject orderOverride)
        {
            if (VanillaSeries.Contains(seriesName))
            {
                PopulateVanillaPlaylists(songData, msgBgmEntries, seriesName, playlistData, coreBgmIds, coreBgmOverride, orderOverride);
                if (stageOverride != null)
                    PopulateStageDatabaseEntries(songData, seriesName, stageOverride, playlistData);
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

        private void PopulateStageDatabaseEntries(JObject songData, string seriesName, JObject stageOverride, JObject playlistData)
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

                var chosenBgm = validPlaylistsStage.Contains(bgmSetId) || playlistData[bgmSetId] != null
                    ? bgmSetId
                    : defaultPlaylistStage;

                GetArray(songData, "stage_database_entries").Add(new JObject
                {
                    ["ui_stage_id"] = stageId,
                    ["bgm_set_id"] = chosenBgm
                });
            }
        }

        private void PopulateVanillaPlaylists(JObject songData, List<string> msgBgmEntries, string seriesName, JObject playlistData, HashSet<string> coreBgmIds, JObject coreBgmOverride, JObject orderOverride)
        {
            var playlists = SeriesToPlaylist.ContainsKey(seriesName.ToLowerInvariant())
                ? SeriesToPlaylist[seriesName.ToLowerInvariant()]
                : new List<string>();

            foreach (var playlistId in playlists)
            {
                var playlistEntries = EnsurePlaylist(songData, playlistId);
                var tracks = GetArray(playlistData[playlistId], "tracks");

                foreach (JObject track in tracks)
                {
                    var uiBgmId = GetString(track, "ui_bgm_id");
                    if (!coreBgmIds.Contains(uiBgmId))
                        continue;

                    AddCoreBgmFromState(songData, uiBgmId, coreBgmOverride, orderOverride);

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

        private void AddCoreBgmFromState(JObject songData, string uiBgmId, JObject coreBgmOverride, JObject orderOverride)
        {
            if (string.IsNullOrEmpty(uiBgmId))
                return;

            if (IsCoreBgmOverride(coreBgmOverride, uiBgmId) || HasBgmDatabaseEntry(songData, uiBgmId))
                return;

            var bgmEntries = GetArray(songData, "bgm_database_entries");
            var db = _audioStateService.GetBgmDbRootEntries()
                .FirstOrDefault(p => string.Equals(p.UiBgmId, uiBgmId, StringComparison.OrdinalIgnoreCase));
            if (db == null)
                return;

            bgmEntries.Add(new JObject
            {
                ["ui_bgm_id"] = db.UiBgmId,
                ["clone_from_ui_bgm_id"] = CloneBgmId,
                ["stream_set_id"] = db.StreamSetId,
                ["name_id"] = db.NameId,
                ["ui_gametitle_id"] = db.UiGameTitleId,
                ["test_disp_order"] = orderOverride != null ? GetInt(orderOverride, uiBgmId, db.TestDispOrder) : db.TestDispOrder,
                ["record_type"] = db.RecordType
            });
        }

        private static bool IsCoreBgmOverride(JObject coreBgmOverride, string uiBgmId)
        {
            if (coreBgmOverride == null || string.IsNullOrEmpty(uiBgmId))
                return false;

            var dbRoots = coreBgmOverride["CoreBgmDbRootOverrides"] as JObject;
            return dbRoots != null && dbRoots[uiBgmId] != null;
        }

        private static bool HasBgmDatabaseEntry(JObject songData, string uiBgmId)
        {
            if (string.IsNullOrEmpty(uiBgmId))
                return false;

            return GetArray(songData, "bgm_database_entries")
                .Any(p => string.Equals(GetString(p, "ui_bgm_id"), uiBgmId, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddUniqueJObjectByKey(JObject songData, string arrayName, string key, JObject entry)
        {
            if (entry == null)
                return;

            var entryKey = GetString(entry, key);
            var entries = GetArray(songData, arrayName);
            if (!string.IsNullOrEmpty(entryKey) && entries.Any(p => string.Equals(GetString(p, key), entryKey, StringComparison.OrdinalIgnoreCase)))
                return;

            entries.Add(entry);
        }

        private static bool HasMessageEntry(List<string> entries, string label)
        {
            if (string.IsNullOrEmpty(label))
                return false;

            var pattern = $"<entry label=\"{label}\">";
            return entries.Any(p => p.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static void AddUniqueMessage(List<string> entries, string label, string text)
        {
            if (string.IsNullOrEmpty(text) || HasMessageEntry(entries, label))
                return;

            entries.Add(MakeEntry(label, EscapeXml(text)));
        }

        private static void AddOptionalBgmMessageUnique(List<string> entries, string label, JToken localizedText)
        {
            var text = GetString(localizedText, "us_en");
            AddUniqueMessage(entries, label, text);
        }

        private static void AddOptionalBgmMessagesFromState(List<string> msgBgmEntries, BgmDbRootEntry db)
        {
            if (db == null || string.IsNullOrEmpty(db.NameId))
                return;

            AddOptionalLocalizedMessage(msgBgmEntries, $"bgm_title_{db.NameId}", db.Title);
            AddOptionalLocalizedMessage(msgBgmEntries, $"bgm_author_{db.NameId}", db.Author);
            AddOptionalLocalizedMessage(msgBgmEntries, $"bgm_copyright_{db.NameId}", db.Copyright);
        }

        private static void AddOptionalLocalizedMessage(List<string> entries, string label, Dictionary<string, string> localizedText)
        {
            if (localizedText == null)
                return;

            var text = localizedText.ContainsKey("us_en") ? localizedText["us_en"] : null;
            if (!string.IsNullOrEmpty(text))
                entries.Add(MakeEntry(label, EscapeXml(text)));
        }

        private void AddCoreStreamEntriesFromState(JObject songData, string streamSetId)
        {
            if (string.IsNullOrEmpty(streamSetId))
                return;

            var streamSet = _audioStateService.GetBgmStreamSetEntries()
                .FirstOrDefault(p => string.Equals(p.StreamSetId, streamSetId, StringComparison.OrdinalIgnoreCase));
            if (streamSet == null)
                return;

            var streamSetEntries = GetArray(songData, "stream_set_entries");
            if (!streamSetEntries.Any(p => string.Equals(GetString(p, "stream_set_id"), streamSet.StreamSetId, StringComparison.OrdinalIgnoreCase)))
                streamSetEntries.Add(CreateStreamSetEntry(CreateStreamSetObject(streamSet), streamSet.StreamSetId));

            foreach (var infoId in GetStreamSetInfoIds(streamSet))
                AddCoreAssignedInfoFromState(songData, infoId);
        }

        private void AddCoreAssignedInfoFromState(JObject songData, string infoId)
        {
            if (string.IsNullOrEmpty(infoId))
                return;

            var assignedInfo = _audioStateService.GetBgmAssignedInfoEntries()
                .FirstOrDefault(p => string.Equals(p.InfoId, infoId, StringComparison.OrdinalIgnoreCase));
            if (assignedInfo == null)
                return;

            var assignedInfoEntries = GetArray(songData, "assigned_info_entries");
            if (!assignedInfoEntries.Any(p => string.Equals(GetString(p, "info_id"), assignedInfo.InfoId, StringComparison.OrdinalIgnoreCase)))
                assignedInfoEntries.Add(CreateAssignedInfoEntry(CreateAssignedInfoObject(assignedInfo)));

            // Core songs do not need stream_property_entries or bgm_property_entries in the generated JSON.
        }

        private void AddCoreStreamPropertyFromState(JObject songData, string streamId)
        {
            if (string.IsNullOrEmpty(streamId))
                return;

            var streamProperty = _audioStateService.GetBgmStreamPropertyEntries()
                .FirstOrDefault(p => string.Equals(p.StreamId, streamId, StringComparison.OrdinalIgnoreCase));
            if (streamProperty == null)
                return;

            var streamPropertyEntries = GetArray(songData, "stream_property_entries");
            if (!streamPropertyEntries.Any(p => string.Equals(GetString(p, "stream_id"), streamProperty.StreamId, StringComparison.OrdinalIgnoreCase)))
                streamPropertyEntries.Add(CreateStreamPropertyEntry(CreateStreamPropertyObject(streamProperty)));

            AddCoreBgmPropertyFromState(songData, streamProperty.DataName0);
        }

        private void AddCoreBgmPropertyFromState(JObject songData, string nameId)
        {
            if (string.IsNullOrEmpty(nameId))
                return;

            var bgmProperty = _audioStateService.GetBgmPropertyEntries()
                .FirstOrDefault(p => string.Equals(p.NameId, nameId, StringComparison.OrdinalIgnoreCase));
            if (bgmProperty == null)
                return;

            var bgmPropertyEntries = GetArray(songData, "bgm_property_entries");
            if (!bgmPropertyEntries.Any(p => string.Equals(GetString(p, "stream_name"), bgmProperty.NameId, StringComparison.OrdinalIgnoreCase)))
                bgmPropertyEntries.Add(CreateBgmPropertyEntry(CreateBgmPropertyObject(bgmProperty), bgmProperty.NameId));
        }

        private static IEnumerable<string> GetStreamSetInfoIds(BgmStreamSetEntry streamSet)
        {
            return new[]
            {
                streamSet.Info0, streamSet.Info1, streamSet.Info2, streamSet.Info3,
                streamSet.Info4, streamSet.Info5, streamSet.Info6, streamSet.Info7,
                streamSet.Info8, streamSet.Info9, streamSet.Info10, streamSet.Info11,
                streamSet.Info12, streamSet.Info13, streamSet.Info14, streamSet.Info15
            }.Where(p => !string.IsNullOrEmpty(p));
        }

        private int GetNextPlaylistOrder(string seriesName, JObject playlistData)
        {
            var seriesKey = seriesName.ToLowerInvariant();
            var playlistIds = GetFallbackPlaylistIds(seriesName);
            if (playlistIds.Count == 0)
                return 0;

            var maxOrder = -1;
            foreach (var playlistId in playlistIds)
            {
                foreach (JObject track in GetArray(playlistData[playlistId], "tracks"))
                {
                    for (var i = 0; i < 16; i++)
                        maxOrder = Math.Max(maxOrder, GetInt(track, $"o{i}", -1));
                }
            }

            return maxOrder + 1;
        }

        private static List<string> GetFallbackPlaylistIds(string seriesName)
        {
            if (!VanillaSeries.Contains(seriesName))
                return new List<string> { SmashBattlePlaylistId };

            var seriesKey = seriesName.ToLowerInvariant();
            return SeriesToPlaylist.ContainsKey(seriesKey)
                ? SeriesToPlaylist[seriesKey]
                : new List<string> { $"bgm{seriesName}" };
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
                foreach (var fallbackPlaylistId in GetFallbackPlaylistIds(seriesName))
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

        private static JObject CreateAssignedInfoEntry(JObject assigned)
        {
            return new JObject
            {
                ["info_id"] = GetString(assigned, "info_id"),
                ["stream_id"] = GetString(assigned, "stream_id"),
                ["condition"] = GetString(assigned, "condition"),
                ["condition_process"] = GetString(assigned, "condition_process", "sound_condition_process_add"),
                ["start_frame"] = GetInt(assigned, "start_frame", 0),
                ["change_fadein_frame"] = GetInt(assigned, "change_fadein_frame", 0),
                ["change_start_delay_frame"] = GetInt(assigned, "change_start_delay_frame", 0),
                ["change_fadeout_frame"] = GetInt(assigned, "change_fadeout_frame", 60),
                ["change_stop_delay_frame"] = GetInt(assigned, "change_stop_delay_frame", 0),
                ["menu_change_fadein_frame"] = GetInt(assigned, "menu_change_fadein_frame", 0),
                ["menu_change_start_delay_frame"] = GetInt(assigned, "menu_change_start_delay_frame", 0),
                ["menu_change_fadeout_frame"] = GetInt(assigned, "menu_change_fadeout_frame", 60),
                ["menu_change_stop_delay_frame"] = GetInt(assigned, "menu_change_stop_delay_frame", 0)
            };
        }

        private static JObject CreateStreamPropertyEntry(JObject streamProperty)
        {
            var entry = new JObject
            {
                ["stream_id"] = GetString(streamProperty, "stream_id"),
                ["data_name0"] = GetString(streamProperty, "data_name0")
            };

            for (var i = 1; i <= 4; i++)
            {
                var dataName = GetString(streamProperty, $"data_name{i}");
                if (!string.IsNullOrEmpty(dataName))
                    entry[$"data_name{i}"] = dataName;
            }

            var loop = GetInt(streamProperty, "loop", int.MinValue);
            if (loop != int.MinValue)
                entry["loop"] = loop;

            var endPoint = GetString(streamProperty, "end_point");
            if (!string.IsNullOrEmpty(endPoint))
                entry["end_point"] = endPoint;

            var fadeoutFrame = GetInt(streamProperty, "fadeout_frame", int.MinValue);
            if (fadeoutFrame != int.MinValue)
                entry["fadeout_frame"] = fadeoutFrame;

            foreach (var pointKey in new[]
            {
                "start_point_suddendeath", "start_point_transition",
                "start_point0", "start_point1", "start_point2", "start_point3", "start_point4"
            })
            {
                var point = GetString(streamProperty, pointKey);
                if (!string.IsNullOrEmpty(point))
                    entry[pointKey] = point;
            }

            return entry;
        }

        private static JObject CreateBgmPropertyEntry(JObject bgmProperty, string streamName)
        {
            return new JObject
            {
                ["stream_name"] = streamName,
                ["loop_start_ms"] = GetInt(bgmProperty, "loop_start_ms", 0),
                ["loop_start_sample"] = GetInt(bgmProperty, "loop_start_sample", 0),
                ["loop_end_ms"] = GetInt(bgmProperty, "loop_end_ms", 0),
                ["loop_end_sample"] = GetInt(bgmProperty, "loop_end_sample", 0),
                ["duration_ms"] = GetInt(bgmProperty, "total_time_ms", GetInt(bgmProperty, "duration_ms", 0)),
                ["duration_sample"] = GetInt(bgmProperty, "total_samples", GetInt(bgmProperty, "duration_sample", 0))
            };
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

        private void CopySeriesIcon(JObject series, string packRoot)
        {
            var iconFile = GetSeriesIconPath(series);
            if (string.IsNullOrEmpty(iconFile))
                return;

            var destinationFolder = Path.Combine(packRoot, "ui", "replace", "series", "series_0");
            Directory.CreateDirectory(destinationFolder);

            var destination = Path.Combine(destinationFolder, Path.GetFileName(iconFile));
            File.Copy(iconFile, destination, true);
            _logger.LogInformation("[CSK] Copied series icon {IconFile} to {Destination}", iconFile, destination);
        }

        private string GetSeriesIconPath(JObject series)
        {
            var iconFolder = GetMusicIconsFolder();
            if (!Directory.Exists(iconFolder))
                return null;

            foreach (var fileName in GetSeriesIconFileNames(series))
            {
                var path = Path.Combine(iconFolder, fileName);
                if (File.Exists(path))
                    return path;
            }

            return null;
        }

        private IEnumerable<string> GetSeriesIconFileNames(JObject series)
        {
            foreach (var value in new[] { GetString(series, "name_id"), GetString(series, "ui_series_id") })
            {
                var sanitized = GetSeriesIconNamePart(value);
                if (!string.IsNullOrEmpty(sanitized))
                    yield return $"series_0_{sanitized}.bntx";
            }
        }

        private string GetMusicIconsFolder()
        {
            var modPath = _config.CurrentValue.Sma5hMusic.ModPath;
            var fullModPath = Path.GetFullPath(modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var modsFolder = Path.GetDirectoryName(fullModPath);
            if (string.IsNullOrEmpty(modsFolder))
                modsFolder = Path.GetFullPath("Mods");

            return Path.Combine(modsFolder, "MusicIcons");
        }

        private static string GetSeriesIconNamePart(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var seriesName = value.StartsWith(MusicConstants.InternalIds.SERIES_ID_PREFIX, StringComparison.OrdinalIgnoreCase)
                ? value.Substring(MusicConstants.InternalIds.SERIES_ID_PREFIX.Length)
                : value;

            return Regex.Replace(seriesName, @"[^a-zA-Z0-9_]", string.Empty).ToLowerInvariant();
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

        private static void NormalizeCombinedSongData(JObject songData)
        {
            DeduplicateArrayByKey(songData, "series_database_entries", "ui_series_id");
            DeduplicateArrayByKey(songData, "gametitle_database_entries", "ui_gametitle_id");
            DeduplicateArrayByKey(songData, "bgm_database_entries", "ui_bgm_id");
            DeduplicateArrayByKey(songData, "stream_set_entries", "stream_set_id");
            DeduplicateArrayByKey(songData, "assigned_info_entries", "info_id");
            DeduplicateArrayByKey(songData, "stream_property_entries", "stream_id");
            DeduplicateArrayByKey(songData, "bgm_property_entries", "stream_name");
            DeduplicateArrayByKey(songData, "stage_database_entries", "ui_stage_id");
            DeduplicatePlaylists(songData);
        }

        private static void DeduplicateArrayByKey(JObject songData, string arrayName, string key)
        {
            var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenObjects = new HashSet<string>(StringComparer.Ordinal);
            var output = new JArray();

            foreach (JObject entry in GetArray(songData, arrayName))
            {
                var entryKey = GetString(entry, key);
                if (!string.IsNullOrEmpty(entryKey))
                {
                    if (!seenKeys.Add(entryKey))
                        continue;
                }
                else
                {
                    var serialized = entry.ToString(Formatting.None);
                    if (!seenObjects.Add(serialized))
                        continue;
                }

                output.Add(entry);
            }

            songData[arrayName] = output;
        }

        private static void DeduplicatePlaylists(JObject songData)
        {
            var playlists = songData["playlist_entries"] as JObject;
            if (playlists == null)
                return;

            foreach (var playlist in playlists.Properties().ToList())
            {
                var seenBgms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var output = new JArray();

                foreach (JObject entry in playlist.Value as JArray ?? new JArray())
                {
                    var uiBgmId = GetString(entry, "ui_bgm_id");
                    if (string.IsNullOrEmpty(uiBgmId) || seenBgms.Add(uiBgmId))
                        output.Add(entry);
                }

                playlist.Value = output;
            }
        }

        private JObject LoadJsonObject(string path)
        {
            if (!File.Exists(path))
                return null;

            _logger.LogInformation("Loading {Path}", path);
            return JObject.Parse(File.ReadAllText(path));
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

        private static void WriteCombinedXmsbt(string path, IEnumerable<string> entries)
        {
            WriteXmsbt(path, entries.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal));
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

        private static bool GetBool(JToken token, string key, bool fallback)
        {
            if (token == null)
                return fallback;

            var value = token[key];
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            bool output;
            if (bool.TryParse(value.ToString(), out output))
                return output;

            int numericOutput;
            return int.TryParse(value.ToString(), out numericOutput) ? numericOutput != 0 : fallback;
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
            public Dictionary<string, string> CoreGameSeriesById { get; set; }
            public HashSet<string> CoreBgmIds { get; set; }
            public JObject PlaylistData { get; set; }
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

        private enum CskPackBuildMode
        {
            Modular,
            Single
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
