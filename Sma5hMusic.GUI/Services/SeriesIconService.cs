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
            byte[] rgba;

            if (Path.GetExtension(sourceIconPath).Equals(".bntx", StringComparison.OrdinalIgnoreCase))
            {
                // Normalize every imported BNTX to the app's BC7 template. This avoids preview crashes
                // from BC1/BC3/BC4/etc. payload sizes and from formats unsupported by the old preview path.
                rgba = LoadRgbaFromBntx(sourceIconPath);
            }
            else
            {
                rgba = LoadResizedRgba(sourceIconPath);
            }

            WriteBc7BntxFromRgba(rgba, destinationPath);
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
                throw new InvalidDataException("The selected file could not be decoded as an image.");

            var source = ReadRgbaFromBitmap(input);
            return ResizeRgba(source, input.Width, input.Height, IconSize, IconSize);
        }

        private static byte[] LoadRgbaFromBntx(string bntxPath)
        {
            var bntx = File.ReadAllBytes(bntxPath);
            var info = ReadBntxTextureInfo(bntx);

            byte[] rgba;
            if (TryGetCompressedFormat(info.Format, out var compressionFormat, out var bytesPerBlock))
            {
                var swizzledPayload = ExtractSwizzledPayload(bntx, info, bytesPerBlock, 4, 4);
                var linearPayload = UnswizzleBlockData(swizzledPayload, info.Width, info.Height, 4, 4, bytesPerBlock, info.BlockHeight);

                var decoder = new BcDecoder();
                var pixels = decoder.DecodeRaw(linearPayload, info.Width, info.Height, compressionFormat);
                rgba = PixelsToRgba(pixels, compressionFormat);
            }
            else if (TryGetRawFormat(info.Format, out var bytesPerPixel, out var channelOrder))
            {
                var swizzledPayload = ExtractSwizzledPayload(bntx, info, bytesPerPixel, 1, 1);
                var linearPayload = UnswizzleBlockData(swizzledPayload, info.Width, info.Height, 1, 1, bytesPerPixel, info.BlockHeight);
                rgba = RawToRgba(linearPayload, info.Width, info.Height, bytesPerPixel, channelOrder);
            }
            else
            {
                throw new InvalidDataException($"Unsupported BNTX series icon texture format: 0x{info.Format:X4}.");
            }

            if (info.Width != IconSize || info.Height != IconSize)
                rgba = ResizeRgba(rgba, info.Width, info.Height, IconSize, IconSize);

            return rgba;
        }

        private void WriteBc7BntxFromRgba(byte[] rgba, string destinationPath)
        {
            if (rgba == null || rgba.Length != IconSize * IconSize * 4)
                throw new InvalidDataException("The source icon did not produce a valid 256x256 RGBA buffer.");

            var encodedBc7 = EncodeBc7(rgba);
            var swizzledBc7 = SwizzleBlockData(encodedBc7, IconSize, IconSize, 4, 4, Bc7BytesPerBlock, BntxBlockHeight);
            var template = File.ReadAllBytes(GetTemplatePath());
            var payloadOffset = GetTexturePayloadOffset(template);

            if (swizzledBc7.Length != Bc7PayloadSize)
                throw new InvalidDataException("The swizzled BC7 payload did not have the expected 256x256 size.");

            Buffer.BlockCopy(swizzledBc7, 0, template, payloadOffset, swizzledBc7.Length);
            File.WriteAllBytes(destinationPath, template);
        }

        private static byte[] EncodeBc7(byte[] rgba)
        {
            var encoder = new BcEncoder(CompressionFormat.Bc7);
            encoder.OutputOptions.GenerateMipMaps = false;
            encoder.OutputOptions.Quality = CompressionQuality.Balanced;
            encoder.OutputOptions.Format = CompressionFormat.Bc7;

            var encoded = encoder.EncodeToRawBytes(rgba, IconSize, IconSize, PixelFormat.Rgba32, 0, out var mipWidth, out var mipHeight);
            if (mipWidth != IconSize || mipHeight != IconSize || encoded.Length != Bc7PayloadSize)
                throw new InvalidDataException("The BC7 encoder did not produce the expected 256x256 payload.");

            return encoded;
        }

        private static void WritePreviewFromBntx(string bntxPath, string previewPath)
        {
            var rgba = LoadRgbaFromBntx(bntxPath);
            WritePng(rgba, IconSize, IconSize, previewPath);
        }

        private static BntxTextureInfo ReadBntxTextureInfo(byte[] bntx)
        {
            var brtiOffset = FindAscii(bntx, "BRTI");
            if (brtiOffset < 0)
                throw new InvalidDataException("The BNTX series icon does not contain a BRTI texture block.");

            var payloadOffset = GetTexturePayloadOffset(bntx);
            var relocationOffset = FindAscii(bntx, "_RLT");
            var payloadEnd = relocationOffset >= 0 ? relocationOffset : bntx.Length;
            if (payloadEnd <= payloadOffset)
                throw new InvalidDataException("The BNTX series icon does not contain texture payload data.");

            var format = ReadUInt32LE(bntx, brtiOffset + 0x1C);
            var width = checked((int)ReadUInt32LE(bntx, brtiOffset + 0x24));
            var height = checked((int)ReadUInt32LE(bntx, brtiOffset + 0x28));
            var blockHeightLog2 = checked((int)ReadUInt32LE(bntx, brtiOffset + 0x34));
            var blockHeight = blockHeightLog2 >= 0 && blockHeightLog2 < 16 ? 1 << blockHeightLog2 : BntxBlockHeight;

            if (width <= 0 || height <= 0)
                throw new InvalidDataException($"Invalid BNTX texture size: {width}x{height}.");

            return new BntxTextureInfo
            {
                Format = format,
                Width = width,
                Height = height,
                BlockHeight = blockHeight,
                PayloadOffset = payloadOffset,
                PayloadLength = payloadEnd - payloadOffset
            };
        }

        private static int GetTexturePayloadOffset(byte[] bntx)
        {
            var brtdOffset = FindAscii(bntx, "BRTD");
            if (brtdOffset < 0)
                throw new InvalidDataException("The BNTX series icon does not contain a BRTD texture data block.");

            var payloadOffset = brtdOffset + 0x10;
            if (payloadOffset > bntx.Length)
                throw new InvalidDataException("The BNTX series icon has an invalid BRTD texture data block.");

            return payloadOffset;
        }

        private static byte[] ExtractSwizzledPayload(byte[] bntx, BntxTextureInfo info, int bytesPerBlock, int blockWidth, int blockHeight)
        {
            var requiredSize = GetSwizzledSurfaceSize(info.Width, info.Height, blockWidth, blockHeight, bytesPerBlock, info.BlockHeight);
            if (requiredSize > info.PayloadLength)
                throw new InvalidDataException(
                    $"The BNTX texture payload is too small for format 0x{info.Format:X4}. Required {requiredSize} bytes, found {info.PayloadLength} bytes.");

            var payload = new byte[requiredSize];
            Buffer.BlockCopy(bntx, info.PayloadOffset, payload, 0, payload.Length);
            return payload;
        }

        private static byte[] SwizzleBlockData(byte[] linearData, int width, int height, int blockWidth, int blockHeight, int bytesPerBlock, int gobBlockHeight)
        {
            var widthBlocks = DivRoundUp(width, blockWidth);
            var heightBlocks = DivRoundUp(height, blockHeight);
            var output = new byte[GetSwizzledSurfaceSize(width, height, blockWidth, blockHeight, bytesPerBlock, gobBlockHeight)];

            for (var y = 0; y < heightBlocks; y++)
            {
                for (var x = 0; x < widthBlocks; x++)
                {
                    var sourceOffset = (y * widthBlocks + x) * bytesPerBlock;
                    var destinationOffset = GetBlockLinearOffset(x, y, widthBlocks, bytesPerBlock, gobBlockHeight);
                    Buffer.BlockCopy(linearData, sourceOffset, output, destinationOffset, bytesPerBlock);
                }
            }

            return output;
        }

        private static byte[] UnswizzleBlockData(byte[] swizzledData, int width, int height, int blockWidth, int blockHeight, int bytesPerBlock, int gobBlockHeight)
        {
            var widthBlocks = DivRoundUp(width, blockWidth);
            var heightBlocks = DivRoundUp(height, blockHeight);
            var output = new byte[widthBlocks * heightBlocks * bytesPerBlock];

            for (var y = 0; y < heightBlocks; y++)
            {
                for (var x = 0; x < widthBlocks; x++)
                {
                    var sourceOffset = GetBlockLinearOffset(x, y, widthBlocks, bytesPerBlock, gobBlockHeight);
                    var destinationOffset = (y * widthBlocks + x) * bytesPerBlock;
                    Buffer.BlockCopy(swizzledData, sourceOffset, output, destinationOffset, bytesPerBlock);
                }
            }

            return output;
        }

        private static int GetSwizzledSurfaceSize(int width, int height, int blockWidth, int blockHeight, int bytesPerBlock, int gobBlockHeight)
        {
            var widthBlocks = DivRoundUp(width, blockWidth);
            var heightBlocks = DivRoundUp(height, blockHeight);
            var max = 0;

            for (var y = 0; y < heightBlocks; y++)
            {
                for (var x = 0; x < widthBlocks; x++)
                {
                    var end = GetBlockLinearOffset(x, y, widthBlocks, bytesPerBlock, gobBlockHeight) + bytesPerBlock;
                    if (end > max)
                        max = end;
                }
            }

            return max;
        }

        private static bool TryGetCompressedFormat(uint bntxFormat, out CompressionFormat compressionFormat, out int bytesPerBlock)
        {
            // BNTX surface format: high byte family identifies BCn; low byte contains UNORM/SRGB/etc variant.
            switch (bntxFormat >> 8)
            {
                case 0x1A:
                    compressionFormat = CompressionFormat.Bc1;
                    bytesPerBlock = 8;
                    return true;
                case 0x1B:
                    compressionFormat = CompressionFormat.Bc2;
                    bytesPerBlock = 16;
                    return true;
                case 0x1C:
                    compressionFormat = CompressionFormat.Bc3;
                    bytesPerBlock = 16;
                    return true;
                case 0x1D:
                    compressionFormat = CompressionFormat.Bc4;
                    bytesPerBlock = 8;
                    return true;
                case 0x1E:
                    compressionFormat = CompressionFormat.Bc5;
                    bytesPerBlock = 16;
                    return true;
                case 0x20:
                    compressionFormat = CompressionFormat.Bc7;
                    bytesPerBlock = 16;
                    return true;
                default:
                    compressionFormat = default;
                    bytesPerBlock = 0;
                    return false;
            }
        }

        private static bool TryGetRawFormat(uint bntxFormat, out int bytesPerPixel, out RawChannelOrder channelOrder)
        {
            switch (bntxFormat)
            {
                // Common Switch RGBA8 formats. These are included as a safe fallback for uncompressed icons.
                case 0x0B01:
                case 0x0B06:
                    bytesPerPixel = 4;
                    channelOrder = RawChannelOrder.Rgba;
                    return true;
                case 0x0C01:
                case 0x0C06:
                    bytesPerPixel = 4;
                    channelOrder = RawChannelOrder.Bgra;
                    return true;
                default:
                    bytesPerPixel = 0;
                    channelOrder = RawChannelOrder.Rgba;
                    return false;
            }
        }

        private static byte[] PixelsToRgba(ColorRgba32[] pixels, CompressionFormat sourceFormat)
        {
            var rgba = new byte[pixels.Length * 4];
            for (var i = 0; i < pixels.Length; i++)
            {
                var offset = i * 4;

                if (sourceFormat == CompressionFormat.Bc4)
                {
                    // BC4 is a single-channel format. BCnEncoder exposes that channel as red;
                    // if we copy it as RGBA literally, white grayscale icons become red.
                    // Treat it as luminance and replicate R into RGB before re-encoding to BC7.
                    var luminance = pixels[i].r;
                    rgba[offset] = luminance;
                    rgba[offset + 1] = luminance;
                    rgba[offset + 2] = luminance;
                    rgba[offset + 3] = pixels[i].a == 0 ? (byte)255 : pixels[i].a;
                    continue;
                }

                rgba[offset] = pixels[i].r;
                rgba[offset + 1] = pixels[i].g;
                rgba[offset + 2] = pixels[i].b;
                rgba[offset + 3] = pixels[i].a;
            }

            return rgba;
        }

        private static byte[] RawToRgba(byte[] raw, int width, int height, int bytesPerPixel, RawChannelOrder channelOrder)
        {
            if (bytesPerPixel != 4)
                throw new InvalidDataException($"Unsupported raw BNTX bytes-per-pixel value: {bytesPerPixel}.");

            var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                var source = i * 4;
                var destination = i * 4;

                if (channelOrder == RawChannelOrder.Bgra)
                {
                    rgba[destination] = raw[source + 2];
                    rgba[destination + 1] = raw[source + 1];
                    rgba[destination + 2] = raw[source];
                    rgba[destination + 3] = raw[source + 3];
                }
                else
                {
                    rgba[destination] = raw[source];
                    rgba[destination + 1] = raw[source + 1];
                    rgba[destination + 2] = raw[source + 2];
                    rgba[destination + 3] = raw[source + 3];
                }
            }

            return rgba;
        }

        private static byte[] ResizeRgba(byte[] rgba, int sourceWidth, int sourceHeight, int destinationWidth, int destinationHeight)
        {
            var sourceInfo = new SKImageInfo(sourceWidth, sourceHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var sourceBitmap = new SKBitmap(sourceInfo);
            Marshal.Copy(rgba, 0, sourceBitmap.GetPixels(), rgba.Length);

            var destinationInfo = new SKImageInfo(destinationWidth, destinationHeight, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var resized = new SKBitmap(destinationInfo);
            using (var canvas = new SKCanvas(resized))
            using (var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true })
            {
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(sourceBitmap, new SKRect(0, 0, destinationWidth, destinationHeight), paint);
            }

            return ReadRgbaFromBitmap(resized);
        }

        private static byte[] ReadRgbaFromBitmap(SKBitmap bitmap)
        {
            var sourceBytes = new byte[bitmap.RowBytes * bitmap.Height];
            Marshal.Copy(bitmap.GetPixels(), sourceBytes, 0, sourceBytes.Length);

            var rgba = new byte[bitmap.Width * bitmap.Height * 4];
            for (var y = 0; y < bitmap.Height; y++)
                Buffer.BlockCopy(sourceBytes, y * bitmap.RowBytes, rgba, y * bitmap.Width * 4, bitmap.Width * 4);

            return rgba;
        }

        private static void WritePng(byte[] rgba, int width, int height, string outputPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
            using var bitmap = new SKBitmap(imageInfo);
            Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
            using var image = SKImage.FromBitmap(bitmap);
            using var encodedPng = image.Encode(SKEncodedImageFormat.Png, 100);
            if (encodedPng == null)
                throw new InvalidDataException("The BNTX series icon preview could not be encoded as PNG.");

            File.WriteAllBytes(outputPath, encodedPng.ToArray());
        }

        private static uint ReadUInt32LE(byte[] data, int offset)
        {
            if (offset < 0 || offset + 4 > data.Length)
                throw new InvalidDataException("The BNTX series icon is truncated.");

            return BitConverter.ToUInt32(data, offset);
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

        private sealed class BntxTextureInfo
        {
            public uint Format { get; set; }
            public int Width { get; set; }
            public int Height { get; set; }
            public int BlockHeight { get; set; }
            public int PayloadOffset { get; set; }
            public int PayloadLength { get; set; }
        }

        private enum RawChannelOrder
        {
            Rgba,
            Bgra
        }
    }
}
