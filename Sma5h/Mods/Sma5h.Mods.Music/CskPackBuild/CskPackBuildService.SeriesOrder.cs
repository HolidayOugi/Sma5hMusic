using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
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
        #region Generation

        private void GenerateSeriesOrderPack(
            List<CskModContext> contexts,
            string outputRoot,
            HashSet<string> selectedSeriesKeys,
            Dictionary<string, int> seriesSoundOrder,
            JObject coreSeriesOverride)
        {
            var seriesEntries = CreateCoreOnlyVanillaSeriesOrderEntries(
                contexts,
                selectedSeriesKeys,
                seriesSoundOrder,
                coreSeriesOverride);

            if (seriesEntries.Count == 0)
                return;

            var folderName = contexts.Count > 1
                ? "CSK Packs - Series Order"
                : SanitizePathSegment(
                    $"{contexts.Select(p => p.SafePackName).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? SinglePackFolderName} - Series Order",
                    "Series Order",
                    "series order folder name");
            var databaseFolder = Path.Combine(outputRoot, folderName, "database");
            Directory.CreateDirectory(databaseFolder);

            var songData = CreateSeriesOrderSongData(seriesEntries);
            var outputJsonPath = Path.Combine(databaseFolder, "series_order.json");
            File.WriteAllText(outputJsonPath, JsonConvert.SerializeObject(songData, Formatting.Indented), new UTF8Encoding(false));
            _logger.LogInformation("[CSK] Saved series order pack: {SavedPath}", outputJsonPath);
        }

        #endregion

        #region Series Entries

        private List<JObject> CreateCoreOnlyVanillaSeriesOrderEntries(
            IEnumerable<CskModContext> contexts,
            HashSet<string> selectedSeriesKeys,
            Dictionary<string, int> seriesSoundOrder,
            JObject coreSeriesOverride)
        {
            var excludedVanillaSeries = contexts
                .SelectMany(context => context.SeriesList
                    .Where(series => IsVanillaSeries(GetString(series, "name_id")))
                    .Where(SeriesHasCustomBgms)
                    .Select(series => GetString(series, "name_id")))
                .Where(p => !string.IsNullOrEmpty(p))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return _audioStateService.GetSeriesEntries()
                .Where(series => IsVanillaSeries(series.NameId))
                .Where(series => !string.IsNullOrEmpty(series.UiSeriesId))
                .Where(series => !excludedVanillaSeries.Contains(series.NameId))
                .GroupBy(series => series.UiSeriesId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group
                    .OrderBy(series => series.Source == EntrySource.Core ? 0 : 1)
                    .First())
                .Select(series =>
                {
                    var seriesObject = CreateSeriesObject(series);
                    var dispOrderSound = GetSeriesSoundOrder(seriesSoundOrder, seriesObject);
                    if (dispOrderSound > 127)
                        dispOrderSound = 127;

                    return CreateSeriesDatabaseEntry(seriesObject, coreSeriesOverride, dispOrderSound);
                })
                .OrderBy(entry => GetInt(entry, "disp_order_sound", 0))
                .ThenBy(entry => GetString(entry, "name_id"), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsVanillaSeries(string seriesName)
        {
            return !string.IsNullOrEmpty(seriesName) &&
                   VanillaSeries.Contains(seriesName);
        }

        private static bool SeriesHasCustomBgms(JObject series)
        {
            foreach (JObject game in GetArray(series, "games"))
            {
                foreach (JObject bgm in GetArray(game, "bgms"))
                {
                    var filename = GetString(bgm, "filename");
                    if (!string.IsNullOrWhiteSpace(filename))
                        return true;
                }
            }

            return false;
        }

        private static JObject CreateSeriesOrderSongData(IEnumerable<JObject> seriesEntries)
        {
            return new JObject
            {
                ["series_database_entries"] = new JArray(seriesEntries)
            };
        }

        private static void AddSeriesOrderEntries(JObject songData, IEnumerable<JObject> seriesEntries)
        {
            var entries = GetArray(songData, "series_database_entries");

            foreach (var entry in seriesEntries)
                entries.Add((JObject)entry.DeepClone());
        }

        #endregion

        #region Sound Order

        private Dictionary<string, int> BuildSeriesSoundOrder(IEnumerable<JObject> seriesList, JObject orderOverride)
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

            var index = 0;
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
                    .Select((p, i) => new { p.Key, Index = i })
                    .ToDictionary(p => p.Key, p => p.Index, StringComparer.OrdinalIgnoreCase);

                foreach (var alias in aliases)
                {
                    if (ranked.ContainsKey(alias.Value))
                        ranked[alias.Key] = ranked[alias.Value];
                }

                return ranked;
            }

            var firstCustomOrder = 39;

            return allSeries
                .Where(p => !VanillaSeries.Contains(GetString(p, "name_id")))
                .OrderBy(p => GetSeriesDisplayName(p).ToLowerInvariant())
                .Select((p, i) => new
                {
                    NameId = GetString(p, "name_id"),
                    UiSeriesId = GetString(p, "ui_series_id"),
                    Order = firstCustomOrder + i
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

        #endregion

        #region Helpers

        private static int GetSeriesSoundOrder(Dictionary<string, int> seriesSoundOrder, JObject series)
        {
            var uiSeriesId = GetString(series, "ui_series_id");
            if (!string.IsNullOrEmpty(uiSeriesId) && seriesSoundOrder.ContainsKey(uiSeriesId))
                return seriesSoundOrder[uiSeriesId];

            var nameId = GetString(series, "name_id");
            if (!string.IsNullOrEmpty(nameId) && seriesSoundOrder.ContainsKey(nameId))
                return seriesSoundOrder[nameId];

            return 0;
        }

        private static void SetSeriesOrderKey(Dictionary<string, int> seriesOrder, string key, int value)
        {
            if (string.IsNullOrEmpty(key) || seriesOrder.ContainsKey(key))
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
                ["disp_order"] = GetInt(effectiveSeries, "disp_order", 0),
                ["disp_order_sound"] = dispOrderSound,
                ["save_no"] = GetInt(effectiveSeries, "save_no", 0),
                ["shown_as_series_in_directory"] = false,
                ["is_dlc"] = GetBool(effectiveSeries, "is_dlc", isDlcSeries),
                ["is_patch"] = GetBool(effectiveSeries, "is_patch", isDlcSeries),
                ["is_use_amiibo_bg"] = GetBool(effectiveSeries, "is_use_amiibo_bg", false)
            };

            var dlcCharaId = GetString(effectiveSeries, "dlc_chara_id");
            if (!string.IsNullOrEmpty(dlcCharaId))
                entry["dlc_chara_id"] = dlcCharaId;

            return entry;
        }

        #endregion
    }
}
