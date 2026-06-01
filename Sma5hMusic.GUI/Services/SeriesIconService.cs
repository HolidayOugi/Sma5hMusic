using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.Shared;
using Microsoft.Extensions.Options;
using SkiaSharp;
using Sma5h.Mods.Music;
using Sma5h.Mods.Music.Helpers;
using Sma5hMusic.GUI.Interfaces;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Sma5hMusic.GUI.Services
{
    public class SeriesIconService : ISeriesIconService
    {
        private const int IconSize = 256;
        private const int Bc7BytesPerBlock = 16;
        private const int Bc7PayloadSize = 65536;
        private const int BntxBlockHeight = 8;
        private const string TemplateFileName = "series_icon_template.bntx";

        private readonly IOptionsMonitor<ApplicationSettings> _config;

        public SeriesIconService(IOptionsMonitor<ApplicationSettings> config)
        {
            _config = config;
        }

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

            return Path.Combine(GetIconFolder(), Path.ChangeExtension(GetIconFileName(uiSeriesId), ".png"));
        }

        public string CreatePreviewFromBntx(string uiSeriesId)
        {
            var iconPath = GetIconPath(uiSeriesId);
            if (string.IsNullOrEmpty(iconPath) || !File.Exists(iconPath))
                return string.Empty;

            var previewPath = GetIconPreviewPath(uiSeriesId);
            Directory.CreateDirectory(Path.GetDirectoryName(previewPath));
            WritePreviewFromBntx(iconPath, previewPath);
            return previewPath;
        }

        public string CreatePreviewFromBntxFile(string bntxPath)
        {
            if (string.IsNullOrEmpty(bntxPath) || !File.Exists(bntxPath))
                return string.Empty;

            var previewFolder = Path.Combine(Path.GetTempPath(), "Sm5shMusic", "SeriesIconPreviews");
            Directory.CreateDirectory(previewFolder);

            var previewPath = Path.Combine(previewFolder, $"{Path.GetFileNameWithoutExtension(bntxPath)}.png");
            WritePreviewFromBntx(bntxPath, previewPath);
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
            if (Path.GetExtension(sourceIconPath).Equals(".bntx", StringComparison.OrdinalIgnoreCase))
            {
                if (!Path.GetFullPath(sourceIconPath).Equals(Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase))
                    File.Copy(sourceIconPath, destinationPath, true);

                WritePreviewFromBntx(destinationPath, GetIconPreviewPath(uiSeriesId));
                return destinationPath;
            }

            var rgba = LoadResizedRgba(sourceIconPath);
            var encodedBc7 = EncodeBc7(rgba);
            var swizzledBc7 = SwizzleBc7(encodedBc7);
            var template = File.ReadAllBytes(GetTemplatePath());
            var payloadOffset = GetTexturePayloadOffset(template);

            Buffer.BlockCopy(swizzledBc7, 0, template, payloadOffset, swizzledBc7.Length);
            File.WriteAllBytes(destinationPath, template);
            WritePreviewFromBntx(destinationPath, GetIconPreviewPath(uiSeriesId));
            return destinationPath;
        }

        private string GetIconFolder()
        {
            var modPath = _config.CurrentValue.Sma5hMusic.ModPath;
            var fullModPath = Path.GetFullPath(modPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var modsFolder = Path.GetDirectoryName(fullModPath);
            if (string.IsNullOrEmpty(modsFolder))
                modsFolder = Path.GetFullPath("Mods");

            return Path.Combine(modsFolder, "MusicIcons");
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

        private static byte[] LoadResizedRgba(string sourcePngPath)
        {
            using var input = SKBitmap.Decode(sourcePngPath);
            if (input == null)
                throw new InvalidDataException("The selected file could not be decoded as a PNG image.");

            var imageInfo = new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var resized = new SKBitmap(imageInfo);
            using (var canvas = new SKCanvas(resized))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(input, new SKRect(0, 0, IconSize, IconSize), paint);
            }

            var sourceBytes = new byte[resized.RowBytes * resized.Height];
            Marshal.Copy(resized.GetPixels(), sourceBytes, 0, sourceBytes.Length);

            var rgba = new byte[IconSize * IconSize * 4];
            for (var y = 0; y < IconSize; y++)
                Buffer.BlockCopy(sourceBytes, y * resized.RowBytes, rgba, y * IconSize * 4, IconSize * 4);

            return rgba;
        }

        private static byte[] EncodeBc7(byte[] rgba)
        {
            var encoder = new BcEncoder(CompressionFormat.Bc7);
            encoder.OutputOptions.GenerateMipMaps = false;
            // Switch-Toolbox's "Normal" quality corresponds to BCnEncoder's balanced preset.
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.OutputOptions.Format = CompressionFormat.Bc7;

            var encoded = encoder.EncodeToRawBytes(rgba, IconSize, IconSize, PixelFormat.Rgba32, 0, out var mipWidth, out var mipHeight);
            if (mipWidth != IconSize || mipHeight != IconSize || encoded.Length != Bc7PayloadSize)
                throw new InvalidDataException("The BC7 encoder did not produce the expected 256x256 payload.");

            return encoded;
        }

        private static int GetTexturePayloadOffset(byte[] template)
        {
            var brtdOffset = FindAscii(template, "BRTD");
            if (brtdOffset < 0)
                throw new InvalidDataException("The BNTX series icon template does not contain a BRTD texture data block.");

            var payloadOffset = brtdOffset + 0x10;
            var payloadEnd = payloadOffset + Bc7PayloadSize;
            if (payloadEnd > template.Length)
                throw new InvalidDataException("The BNTX series icon template is smaller than the expected BC7 payload.");

            var relocationOffset = FindAscii(template, "_RLT");
            if (relocationOffset >= 0 && payloadEnd > relocationOffset)
                throw new InvalidDataException("The BC7 payload would overwrite the BNTX relocation table.");

            return payloadOffset;
        }

        private static int FindAscii(byte[] data, string value)
        {
            var marker = Encoding.ASCII.GetBytes(value);
            for (var i = 0; i <= data.Length - marker.Length; i++)
            {
                var found = true;
                for (var j = 0; j < marker.Length; j++)
                {
                    if (data[i + j] != marker[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                    return i;
            }

            return -1;
        }

        private static byte[] SwizzleBc7(byte[] linearData)
        {
            var widthBlocks = IconSize / 4;
            var heightBlocks = IconSize / 4;
            var output = new byte[Bc7PayloadSize];

            for (var y = 0; y < heightBlocks; y++)
            {
                for (var x = 0; x < widthBlocks; x++)
                {
                    var sourceOffset = (y * widthBlocks + x) * Bc7BytesPerBlock;
                    var destinationOffset = GetBlockLinearOffset(x, y, widthBlocks, Bc7BytesPerBlock, BntxBlockHeight);
                    Buffer.BlockCopy(linearData, sourceOffset, output, destinationOffset, Bc7BytesPerBlock);
                }
            }

            return output;
        }

        private static byte[] UnswizzleBc7(byte[] swizzledData)
        {
            var widthBlocks = IconSize / 4;
            var heightBlocks = IconSize / 4;
            var output = new byte[Bc7PayloadSize];

            for (var y = 0; y < heightBlocks; y++)
            {
                for (var x = 0; x < widthBlocks; x++)
                {
                    var sourceOffset = GetBlockLinearOffset(x, y, widthBlocks, Bc7BytesPerBlock, BntxBlockHeight);
                    var destinationOffset = (y * widthBlocks + x) * Bc7BytesPerBlock;
                    Buffer.BlockCopy(swizzledData, sourceOffset, output, destinationOffset, Bc7BytesPerBlock);
                }
            }

            return output;
        }

        private static void WritePreviewFromBntx(string bntxPath, string previewPath)
        {
            var bntx = File.ReadAllBytes(bntxPath);
            var payloadOffset = GetTexturePayloadOffset(bntx);
            var swizzledPayload = new byte[Bc7PayloadSize];
            Buffer.BlockCopy(bntx, payloadOffset, swizzledPayload, 0, swizzledPayload.Length);

            var linearPayload = UnswizzleBc7(swizzledPayload);
            var decoder = new BcDecoder();
            var pixels = decoder.DecodeRaw(linearPayload, IconSize, IconSize, CompressionFormat.Bc7);

            var rgba = new byte[IconSize * IconSize * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var offset = i * 4;
                rgba[offset] = pixels[i].r;
                rgba[offset + 1] = pixels[i].g;
                rgba[offset + 2] = pixels[i].b;
                rgba[offset + 3] = pixels[i].a;
            }

            var imageInfo = new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(imageInfo);
            Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var encodedPng = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encodedPng == null)
                throw new InvalidDataException("The BNTX series icon preview could not be encoded as PNG.");

            File.WriteAllBytes(previewPath, encodedPng.ToArray());
        }

        private static int GetBlockLinearOffset(int x, int y, int widthBlocks, int bytesPerBlock, int blockHeight)
        {
            var widthInGobs = DivRoundUp(widthBlocks * bytesPerBlock, 64);
            var xBytes = x * bytesPerBlock;
            var gobAddress =
                (y / (8 * blockHeight)) * 512 * blockHeight * widthInGobs +
                (xBytes / 64) * 512 * blockHeight +
                ((y % (8 * blockHeight)) / 8) * 512;

            return gobAddress +
                   ((xBytes % 64) / 32) * 256 +
                   ((y % 8) / 2) * 64 +
                   ((xBytes % 32) / 16) * 32 +
                   (y % 2) * 16 +
                   (xBytes % 16);
        }

        private static int DivRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }
    }
}
