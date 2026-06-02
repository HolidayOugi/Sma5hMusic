using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Sma5h.Mods.Music.Helpers
{
    public static class CskPathSanitizer
    {
        private const string DefaultFallback = "unnamed";

        private static readonly HashSet<char> InvalidPathSegmentChars = new HashSet<char>(new[]
        {
            '<', '>', ':', '\"', '/', '\\', '|', '?', '*'
        });

        private static readonly HashSet<string> ReservedWindowsNames = new HashSet<string>(new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        }, StringComparer.OrdinalIgnoreCase);

        public static string SanitizePathSegment(string value, string fallback)
        {
            var text = string.IsNullOrWhiteSpace(value) ? fallback : value;
            if (string.IsNullOrWhiteSpace(text))
                text = DefaultFallback;

            text = RemoveDiacritics(text);

            var builder = new StringBuilder(text.Length);
            foreach (var c in text)
            {
                if (!InvalidPathSegmentChars.Contains(c) && !char.IsControl(c))
                    builder.Append(c);
            }

            var sanitized = Regex.Replace(builder.ToString(), @"\s+", " ").Trim();
            sanitized = sanitized.TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(sanitized))
                sanitized = DefaultFallback;

            if (IsReservedWindowsName(sanitized))
                sanitized = $"_{sanitized}";

            return sanitized;
        }

        private static string RemoveDiacritics(string value)
        {
            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);

            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static bool IsReservedWindowsName(string value)
        {
            var name = value.Split('.')[0].Trim();
            return !string.IsNullOrEmpty(name) && ReservedWindowsNames.Contains(name);
        }
    }
}
