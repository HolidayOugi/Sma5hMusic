using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region Audio Files

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

        #endregion

        #region Series Icons

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

        #endregion

        #region Utils

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

        #endregion

    }
}
