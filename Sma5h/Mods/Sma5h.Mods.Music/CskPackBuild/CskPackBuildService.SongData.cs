using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Creation

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

        #endregion

        #region Normalization

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

        #endregion

    }
}
