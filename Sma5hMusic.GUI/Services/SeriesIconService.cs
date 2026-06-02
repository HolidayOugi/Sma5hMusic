using Microsoft.Extensions.Options;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5h.Mods.Music.Services;
using Sma5hMusic.GUI.Interfaces;
using System;
using System.IO;
using System.Text.RegularExpressions;

namespace Sma5hMusic.GUI.Services
{
    public class SeriesIconService : ISeriesIconService
    {
        private const string TemplateFileName = "series_icon_template.bntx";

        private readonly IOptionsMonitor<ApplicationSettings> _config;

        public SeriesIconService(IOptionsMonitor<ApplicationSettings> config)
        {
            _config = config;
        }

        #region Public

        public string GetIconPath(string uiSeriesId)
        {
            if (string.IsNullOrEmpty(uiSeriesId))
                return string.Empty;

            return Path.Combine(GetIconFolder(), GetIconFileName(uiSeriesId));
        }

        public string GetIconPreviewPath(string uiSeriesId)
        {
            if (string.IsNullOrEmpty(uiSeriesId))
                return string.Empty;

            return Path.Combine(GetPreviewFolder(), Path.ChangeExtension(GetIconFileName(uiSeriesId), ".png"));
        }

        public string CreatePreviewFromBntx(string uiSeriesId)
        {
            var iconPath = GetIconPath(uiSeriesId);
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                return string.Empty;

            var previewPath = GetIconPreviewPath(uiSeriesId);
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
            SeriesIconBntxCodec.WritePreviewFromBntx(iconPath, previewPath);
            return previewPath;
        }

        public string CreatePreviewFromBntxFile(string bntxPath)
        {
            if (string.IsNullOrEmpty(bntxPath) || !File.Exists(bntxPath))
                return string.Empty;

            var previewFolder = GetPreviewFolder();
            Directory.CreateDirectory(previewFolder);

            var previewPath = Path.Combine(previewFolder, $"{Path.GetFileNameWithoutExtension(bntxPath)}.png");
            SeriesIconBntxCodec.WritePreviewFromBntx(bntxPath, previewPath);
            return previewPath;
        }

        public string SaveIcon(string sourceIconPath, string uiSeriesId)
        {
            if (string.IsNullOrEmpty(sourceIconPath))
                return GetIconPath(uiSeriesId);

            if (!File.Exists(sourceIconPath))
                throw new FileNotFoundException("The selected series icon file was not found.", sourceIconPath);

            var iconFolder = GetIconFolder();
            Directory.CreateDirectory(iconFolder);

            var destinationPath = Path.Combine(iconFolder, GetIconFileName(uiSeriesId));
            byte[] rgba;

            if (Path.GetExtension(sourceIconPath).Equals(".bntx", StringComparison.OrdinalIgnoreCase))
            {
                // Normalize every imported BNTX to the app's BC7 template. This avoids preview crashes
                // from BC1/BC3/BC4/etc. payload sizes and from formats unsupported by the old preview path.
                rgba = SeriesIconBntxCodec.LoadRgbaFromBntx(sourceIconPath);
            }
            else
            {
                rgba = SeriesIconBntxCodec.LoadResizedRgba(sourceIconPath);
            }

            SeriesIconBntxCodec.WriteBc7BntxFromRgba(rgba, GetTemplatePath(), destinationPath);
            DeleteLegacyIconPreview(uiSeriesId);
            return destinationPath;
        }

        #endregion

        #region Paths

        private string GetIconFolder()
        {
            var modPath = _config.CurrentValue.Sma5hMusic.ModPath;
            var fullModPath = Path.GetFullPath(modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var modsFolder = Path.GetDirectoryName(fullModPath);
            if (string.IsNullOrEmpty(modsFolder))
                modsFolder = Path.GetFullPath("Mods");

            return Path.Combine(modsFolder, "MusicIcons");
        }

        private static string GetPreviewFolder()
        {
            return Path.Combine(Path.GetTempPath(), "Sm5shMusic", "SeriesIconPreviews");
        }

        private void DeleteLegacyIconPreview(string uiSeriesId)
        {
            var legacyPreviewPath = Path.Combine(GetIconFolder(), Path.ChangeExtension(GetIconFileName(uiSeriesId), ".png"));
            if (File.Exists(legacyPreviewPath))
                File.Delete(legacyPreviewPath);
        }

        private static string GetIconFileName(string uiSeriesId)
        {
            var seriesName = uiSeriesId.StartsWith(MusicConstants.InternalIds.SERIES_ID_PREFIX, StringComparison.OrdinalIgnoreCase)
                ? uiSeriesId.Substring(MusicConstants.InternalIds.SERIES_ID_PREFIX.Length)
                : uiSeriesId;
            seriesName = Regex.Replace(seriesName, @"[^a-zA-Z0-9_]", string.Empty).ToLowerInvariant();
            return $"series_0_{seriesName}.bntx";
        }

        private string GetTemplatePath()
        {
            var templatePath = Path.Combine(_config.CurrentValue.ResourcesPath, TemplateFileName);
            if (!File.Exists(templatePath))
                throw new FileNotFoundException("The BNTX series icon template was not found.", templatePath);

            return templatePath;
        }

        #endregion
    }
}
