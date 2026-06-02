using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Processing

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

        #endregion

        #region Vanilla Playlists

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

        #endregion

        #region Core BGM

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

        #endregion

        #region Messages

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

        #endregion

        #region Core Entries

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

        #endregion

        #region Playlist Helpers

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

        #endregion

    }
}
