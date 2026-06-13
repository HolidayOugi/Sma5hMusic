using ClosedXML.Excel;
using Sma5h.Mods.Music.Interfaces;
using Sma5h.Mods.Music.Models;
using Sma5hMusic.GUI.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Sma5hMusic.GUI.Services
{
    public class SongSpreadsheetService : ISongSpreadsheetService
    {
        private const string MainMenuPlaylistId = "bgmsmashmenu";

        private static readonly string[][] ColorFamilies =
        {
            new[] { "E8F0FE", "E1F5FE", "ECEFF1", "F5F5F5" },
            new[] { "E6F4EA", "E8F5E9" },
            new[] { "FFF4CE", "FFF3E0" },
            new[] { "FCE8E6", "FCE4EC", "EDE7F6", "F3E5F5" }
        };

        private static readonly Regex DoubleBracesRegex = new Regex(@"\{\{(.*?)\}\}", RegexOptions.Compiled);
        private static readonly Regex SongSuffixRegex = new Regex(@"\s[-–—]\s", RegexOptions.Compiled);

        private readonly IAudioStateService _audioState;

        public SongSpreadsheetService(IAudioStateService audioState)
        {
            _audioState = audioState;
        }

        public bool HasSongListEntries(IMusicMod musicMod, string locale)
        {
            return GetSongListRows(musicMod, locale).Count > 0;
        }

        public bool HasPinchSongEntries(IMusicMod musicMod, string locale)
        {
            return GetPinchSongRows(musicMod, locale).Count > 0;
        }

        public bool HasMainMenuSongEntries(IMusicMod musicMod, string locale)
        {
            return GetMainMenuSongRows(musicMod, locale).Count > 0;
        }

        public Task<bool> CreateSongList(string outputPath, IMusicMod musicMod, string locale)
        {
            return Task.Run(() =>
            {
                var rows = GetSongListRows(musicMod, locale);
                if (rows.Count == 0)
                    return false;

                WriteWorkbook(
                    outputPath,
                    "Song List",
                    new[] { "Song", "Game", "Series" },
                    rows.Select(row => new[] { row.Song, row.Game, row.Series }).ToList(),
                    rows.Select(row => row.Series).ToList());

                return true;
            });
        }

        public Task<bool> CreatePinchSongs(string outputPath, IMusicMod musicMod, string locale)
        {
            return Task.Run(() =>
            {
                var rows = GetPinchSongRows(musicMod, locale);
                if (rows.Count == 0)
                    return false;

                WriteWorkbook(
                    outputPath,
                    "Pinch Song",
                    new[] { "Song", "Pinch Song", "Game", "Series" },
                    rows.Select(row => new[] { row.Song, row.PinchSong, row.Game, row.Series }).ToList(),
                    rows.Select(row => row.Series).ToList());

                return true;
            });
        }

        public Task<bool> CreateMainMenuSongs(string outputPath, IMusicMod musicMod, string locale)
        {
            return Task.Run(() =>
            {
                var rows = GetMainMenuSongRows(musicMod, locale);
                if (rows.Count == 0)
                    return false;

                WriteWorkbook(
                    outputPath,
                    "Song List",
                    new[] { "Song", "Game", "Series" },
                    rows.Select(row => new[] { row.Song, row.Game, row.Series }).ToList(),
                    rows.Select(row => row.Series).ToList());

                return true;
            });
        }

        private List<SongRow> GetSongListRows(IMusicMod musicMod, string locale)
        {
            var context = CreateContext();
            return context.Songs
                .Where(song => IsSongFromMod(song, musicMod) && song.TestDispOrder != -1)
                .Select(song => CreateSongRow(song, context, locale))
                .Where(row => row != null)
                .OrderBy(row => row.Series, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Order)
                .ThenBy(row => row.Song, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<PinchSongRow> GetPinchSongRows(IMusicMod musicMod, string locale)
        {
            var context = CreateContext();
            var streamSets = _audioState.GetBgmStreamSetEntries()
                .ToDictionary(streamSet => streamSet.StreamSetId, StringComparer.Ordinal);
            var validInfoIds = new HashSet<string>(
                _audioState.GetBgmAssignedInfoEntries().Select(info => info.InfoId),
                StringComparer.Ordinal);

            var songsByInfoId = context.Songs
                .Select(song => new
                {
                    Song = song,
                    StreamSet = GetValue(streamSets, song.StreamSetId)
                })
                .Where(item => !string.IsNullOrEmpty(item.StreamSet?.Info0) && validInfoIds.Contains(item.StreamSet.Info0))
                .GroupBy(item => item.StreamSet.Info0, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First().Song, StringComparer.Ordinal);

            var rows = new List<PinchSongRow>();
            foreach (var song in context.Songs.Where(song => IsSongFromMod(song, musicMod)))
            {
                var streamSet = GetValue(streamSets, song.StreamSetId);
                if (string.IsNullOrEmpty(streamSet?.Info1))
                    continue;

                var baseRow = CreateSongRow(song, context, locale);
                if (baseRow == null || string.IsNullOrEmpty(baseRow.Game) || string.IsNullOrEmpty(baseRow.Series))
                    continue;

                var pinchSong = GetValue(songsByInfoId, streamSet.Info1);
                var pinchSongName = pinchSong == null
                    ? string.Empty
                    : StripSongSuffix(GetTitle(pinchSong.Title, locale), baseRow.Game);

                rows.Add(new PinchSongRow
                {
                    Song = baseRow.Song,
                    PinchSong = pinchSongName,
                    Game = baseRow.Game,
                    Series = baseRow.Series
                });
            }

            return rows
                .OrderBy(row => row.Series, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Game, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Song, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<SongRow> GetMainMenuSongRows(IMusicMod musicMod, string locale)
        {
            var context = CreateContext();
            var playlist = _audioState.GetPlaylists()
                .FirstOrDefault(item => string.Equals(item.Id, MainMenuPlaylistId, StringComparison.OrdinalIgnoreCase));
            var playlistSongIds = new HashSet<string>(
                playlist?.Tracks.Select(track => NormalizeBgmId(track.UiBgmId)) ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            return context.Songs
                .Where(song => IsSongFromMod(song, musicMod) && playlistSongIds.Contains(NormalizeBgmId(song.UiBgmId)))
                .Select(song => CreateSongRow(song, context, locale))
                .Where(row => row != null)
                .OrderBy(row => row.Series, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Game, StringComparer.OrdinalIgnoreCase)
                .ThenBy(row => row.Song, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool IsSongFromMod(BgmDbRootEntry song, IMusicMod musicMod)
        {
            return song?.MusicMod != null &&
                musicMod != null &&
                string.Equals(song.MusicMod.Id, musicMod.Id, StringComparison.Ordinal);
        }

        private SpreadsheetContext CreateContext()
        {
            return new SpreadsheetContext
            {
                Songs = _audioState.GetBgmDbRootEntries().ToList(),
                Games = _audioState.GetGameTitleEntries()
                    .ToDictionary(game => game.UiGameTitleId, StringComparer.Ordinal),
                Series = _audioState.GetSeriesEntries()
                    .ToDictionary(series => series.UiSeriesId, StringComparer.Ordinal)
            };
        }

        private static SongRow CreateSongRow(BgmDbRootEntry song, SpreadsheetContext context, string locale)
        {
            var songName = GetTitle(song.Title, locale);
            if (string.IsNullOrEmpty(songName))
                return null;

            var game = GetValue(context.Games, song.UiGameTitleId);
            var gameName = GetTitle(game?.MSBTTitle, locale);
            var series = GetValue(context.Series, game?.UiSeriesId);
            var seriesName = GetTitle(series?.MSBTTitle, locale);

            return new SongRow
            {
                Song = StripSongSuffix(UnwrapDoubleBraces(songName), gameName),
                Game = gameName ?? string.Empty,
                Series = seriesName ?? string.Empty,
                Order = song.TestDispOrder
            };
        }

        private static string GetTitle(Dictionary<string, string> titles, string locale)
        {
            if (!string.IsNullOrEmpty(locale) &&
                titles != null &&
                titles.TryGetValue(locale, out var title) &&
                !string.IsNullOrEmpty(title))
                return title;

            if (titles != null &&
                titles.TryGetValue("us_en", out var fallbackTitle) &&
                !string.IsNullOrEmpty(fallbackTitle))
                return fallbackTitle;

            return null;
        }

        private static string UnwrapDoubleBraces(string value)
        {
            return string.IsNullOrEmpty(value) ? value : DoubleBracesRegex.Replace(value, "$1");
        }

        private static string StripSongSuffix(string songName, string gameName)
        {
            if (string.IsNullOrEmpty(songName) || string.IsNullOrEmpty(gameName))
                return songName;

            foreach (Match match in SongSuffixRegex.Matches(songName))
            {
                var suffix = songName.Substring(match.Index + match.Length).Trim();
                if (gameName.StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return songName.Substring(0, match.Index).Trim();
            }

            return songName;
        }

        private static string NormalizeBgmId(string uiBgmId)
        {
            const string prefix = "ui_bgm_";
            return uiBgmId != null && uiBgmId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                ? uiBgmId.Substring(prefix.Length)
                : uiBgmId ?? string.Empty;
        }

        private static TValue GetValue<TKey, TValue>(Dictionary<TKey, TValue> values, TKey key)
        {
            if (key != null && values.TryGetValue(key, out var value))
                return value;

            return default;
        }

        private static void WriteWorkbook(
            string outputPath,
            string sheetName,
            IReadOnlyList<string> headers,
            IReadOnlyList<string[]> rows,
            IReadOnlyList<string> rowSeries)
        {
            var colorsBySeries = CreateSeriesColorMap(rowSeries);

            using var workbook = new XLWorkbook();
            var worksheet = workbook.Worksheets.Add(sheetName);

            for (var column = 0; column < headers.Count; column++)
                worksheet.Cell(1, column + 1).Value = headers[column] ?? string.Empty;

            var headerRange = worksheet.Range(1, 1, 1, headers.Count);
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            headerRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var excelRowNumber = rowIndex + 2;
                var values = rows[rowIndex];
                for (var column = 0; column < values.Length; column++)
                    worksheet.Cell(excelRowNumber, column + 1).Value = values[column] ?? string.Empty;

                var writtenRange = worksheet.Range(excelRowNumber, 1, excelRowNumber, values.Length);
                writtenRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                writtenRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                writtenRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#" + colorsBySeries[rowSeries[rowIndex] ?? string.Empty]);
            }

            var usedColumns = worksheet.Columns(1, headers.Count);
            usedColumns.AdjustToContents();
            foreach (var column in usedColumns)
                column.Width += 2;

            workbook.SaveAs(outputPath);
        }

        private static Dictionary<string, string> CreateSeriesColorMap(IEnumerable<string> rowSeries)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            var seriesNames = rowSeries.Select(series => series ?? string.Empty).Distinct(StringComparer.Ordinal);
            var familyIndex = 0;
            var colorIndex = 0;

            foreach (var seriesName in seriesNames)
            {
                var family = ColorFamilies[familyIndex];
                result[seriesName] = family[colorIndex % family.Length];
                familyIndex = (familyIndex + 1) % ColorFamilies.Length;
                colorIndex++;
            }

            return result;
        }

        private class SpreadsheetContext
        {
            public List<BgmDbRootEntry> Songs { get; set; }
            public Dictionary<string, GameTitleEntry> Games { get; set; }
            public Dictionary<string, SeriesEntry> Series { get; set; }
        }

        private class SongRow
        {
            public string Song { get; set; }
            public string Game { get; set; }
            public string Series { get; set; }
            public int Order { get; set; }
        }

        private class PinchSongRow
        {
            public string Song { get; set; }
            public string PinchSong { get; set; }
            public string Game { get; set; }
            public string Series { get; set; }
        }
    }
}
