using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Models;
using Sma5h.Mods.Music.Models.PlaylistEntryModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Loading

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

        #endregion

        #region Effective Data

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

        #endregion

        #region Playlist Objects

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

        #endregion

        #region Database Objects

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
                ["disp_order"] = series.DispOrder,
                ["disp_order_sound"] = series.DispOrderSound,
                ["save_no"] = series.SaveNo,
                ["0x1c38302364"] = series.Unk1,
                ["is_dlc"] = series.IsDlc,
                ["is_patch"] = series.IsPatch,
                ["dlc_chara_id"] = series.DlcCharaId,
                ["is_use_amiibo_bg"] = series.IsUseAmiiboBg,
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

        #endregion

        #region Localized Objects

        private static JObject CreateLocalizedObject(Dictionary<string, string> localizedText)
        {
            var output = new JObject();
            if (localizedText == null)
                return output;

            foreach (var entry in localizedText)
                output[entry.Key] = entry.Value;

            return output;
        }

        #endregion

        #region Merge Helpers

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

        #endregion

    }
}
