using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Moved Core Games

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

        #endregion

        #region Core BGM Overrides

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

        #endregion

    }
}
