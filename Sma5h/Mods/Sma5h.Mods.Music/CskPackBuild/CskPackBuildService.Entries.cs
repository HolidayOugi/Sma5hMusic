using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music.Models;
using System;
using System.Linq;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Game Entries

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

        #endregion

        #region BGM Entries

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

        #endregion

    }
}
