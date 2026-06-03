using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Pack Generation

        private void GenerateCskPacks(IEnumerable<CskModContext> contexts, string generatedBgmFolder, string outputRoot, HashSet<string> selectedSeriesKeys, CskBuildResources buildResources)
        {
            var contextList = contexts.ToList();
            var allSeries = contextList.SelectMany(context => context.SeriesList).ToList();
            var seriesSoundOrder = BuildSeriesSoundOrder(
                allSeries,
                buildResources.OrderOverride);
            var seriesDatabaseFileCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var context in contextList)
            {
                _logger.LogInformation("Generating CSK packs from {MetadataPath}", context.MetadataPath);

                foreach (var series in context.SeriesList.Where(series => selectedSeriesKeys.Contains(CreateSeriesKey(context.Mod, series))))
                {
                    var seriesName = GetString(series, "name_id");
                    var databaseFileBaseName = SanitizePathSegment(seriesName, "series", "series database file name");
                    seriesDatabaseFileCounts.TryGetValue(databaseFileBaseName, out var databaseFileCount);
                    databaseFileCount++;
                    seriesDatabaseFileCounts[databaseFileBaseName] = databaseFileCount;
                    var databaseFileName = databaseFileCount == 1
                        ? $"{databaseFileBaseName}.json"
                        : $"{databaseFileBaseName}{databaseFileCount}.json";

                    var savedPath = ProcessSeries(
                        series,
                        context.SafePackName,
                        databaseFileName,
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

            GenerateSeriesOrderPack(
                contextList,
                outputRoot,
                selectedSeriesKeys,
                seriesSoundOrder,
                buildResources.CoreSeriesOverride);
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

            var seriesSoundOrder = BuildSeriesSoundOrder(allSeries, buildResources.OrderOverride);
            var seriesIdToName = contextList
                .SelectMany(context => context.SeriesIdToName)
                .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(p => p.Key, p => p.First().Value, StringComparer.OrdinalIgnoreCase);

            var singlePackFolderName = GetSingleCskPackFolderName(contextList);
            var packRoot = Path.Combine(outputRoot, singlePackFolderName);
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
                    singlePackFolderName,
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

            var coreOnlyVanillaSeriesOrderEntries = CreateCoreOnlyVanillaSeriesOrderEntries(
                contextList,
                selectedSeriesKeys,
                seriesSoundOrder,
                buildResources.CoreSeriesOverride);
            AddSeriesOrderEntries(songData, coreOnlyVanillaSeriesOrderEntries);

            NormalizeCombinedSongData(songData);
            WriteCombinedXmsbt(Path.Combine(uiFolder, "msg_bgm.xmsbt"), msgBgmEntries);
            WriteCombinedXmsbt(Path.Combine(uiFolder, "msg_title.xmsbt"), msgTitleEntries);

            var outputJsonPath = Path.Combine(databaseFolder, "song_data.json");
            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(songData, Formatting.Indented), new UTF8Encoding(false));
            _logger.LogInformation("[CSK] Saved single CSK pack: {SavedPath}", outputJsonPath);
        }

        private static string GetSingleCskPackFolderName(IReadOnlyList<CskModContext> contexts)
        {
            if (contexts.Count == 1 && !string.IsNullOrWhiteSpace(contexts[0].SafePackName))
                return contexts[0].SafePackName;

            return SinglePackFolderName;
        }

        #endregion

        #region Pack Helpers

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

        #endregion

        #region Series Processing

        private string ProcessSeries(
            JObject series,
            string packName,
            string databaseFileName,
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

            var outputJsonPath = Path.Combine(seriesDbFolder, databaseFileName);
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

        #endregion

        #region BGM Processing

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

        #endregion

    }
}
