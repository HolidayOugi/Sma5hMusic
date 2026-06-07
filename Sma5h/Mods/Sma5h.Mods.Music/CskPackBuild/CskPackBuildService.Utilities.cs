using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using Sma5h.Mods.Music.Helpers;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Sma5h.Mods.Music.CskPackBuild
{
    public partial class CskPackBuildService
    {
        #region JSON

        private JObject LoadJsonObject(string path)
        {
            if (!File.Exists(path))
                return null;

            _logger.LogInformation("Loading {Path}", path);
            return JObject.Parse(File.ReadAllText(path));
        }

        #endregion

        #region Messages

        private void AddOptionalBgmMessage(List<string> entries, string label, JToken localizedText)
        {
            var text = GetLocalizedString(localizedText);
            if (!string.IsNullOrEmpty(text))
                entries.Add(MakeEntry(label, EscapeXml(text)));
        }

        private static string MakeEntry(string label, string text)
        {
            return $"<entry label=\"{label}\">\r\n<text>{text}</text>\r\n</entry>";
        }

        private static void WriteXmsbt(string path, IEnumerable<string> entries)
        {
            var content = new StringBuilder();
            content.Append("<?xml version=\"1.0\" encoding=\"utf-16\"?>\n<xmsbt>\n");
            foreach (var entry in entries)
                content.Append(entry).Append("\n");
            content.Append("</xmsbt>");
            File.WriteAllText(path, content.ToString(), Encoding.Unicode);
        }

        private static void WriteCombinedXmsbt(string path, IEnumerable<string> entries)
        {
            WriteXmsbt(path, entries.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.Ordinal));
        }

        private static string EscapeXml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            text = Regex.Replace(text, "\\{\\{(.*?)\\}\\}", "$1");
            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("'", "&apos;")
                .Replace("\"", "&quot;");
        }

        #endregion

        #region Accessors

        private static string GetString(JToken token, string key, string fallback = "")
        {
            var value = GetChildValue(token, key);
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            return value.ToString();
        }

        private string GetLocalizedString(JToken localizedText, string fallback = "")
        {
            if (localizedText == null)
                return fallback;

            foreach (var locale in GetCskTextLocales())
            {
                var text = GetString(localizedText, locale);
                if (!string.IsNullOrWhiteSpace(text))
                    return text;
            }

            return fallback;
        }

        private IEnumerable<string> GetCskTextLocales()
        {
            var configuredLocales = new[]
            {
                _currentBuildLocale.Value,
                _config.CurrentValue.Sma5hMusicGUI?.DefaultMSBTLocale,
                _config.CurrentValue.Sma5hMusic?.DefaultLocale,
                _config.CurrentValue.Sma5hMusicGUI?.DefaultGUILocale,
                "us_en",
                "eu_en"
            };

            return configuredLocales
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private static int GetInt(JToken token, string key, int fallback)
        {
            var value = GetChildValue(token, key);
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            int output;
            return int.TryParse(value.ToString(), out output) ? output : fallback;
        }

        private static float GetFloat(JToken token, string key, float fallback)
        {
            var value = GetChildValue(token, key);
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            if (value.Type == JTokenType.Float || value.Type == JTokenType.Integer)
                return value.Value<float>();

            float output;
            if (float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out output))
                return output;

            return float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.CurrentCulture, out output) ? output : fallback;
        }

        private static bool GetBool(JToken token, string key, bool fallback)
        {
            var value = GetChildValue(token, key);
            if (value == null || value.Type == JTokenType.Null)
                return fallback;

            bool output;
            if (bool.TryParse(value.ToString(), out output))
                return output;

            int numericOutput;
            return int.TryParse(value.ToString(), out numericOutput) ? numericOutput != 0 : fallback;
        }

        private static JArray GetArray(JToken token, string key)
        {
            return GetChildValue(token, key) as JArray ?? new JArray();
        }

        private static JToken GetChildValue(JToken token, string key)
        {
            var obj = token as JObject;
            return obj == null ? null : obj[key];
        }

        #endregion

        #region Paths

        private string SanitizePathSegment(string value, string fallback, string context)
        {
            var sanitized = CskPathSanitizer.SanitizePathSegment(value, fallback);

            if (!string.Equals(value, sanitized, StringComparison.Ordinal))
                _logger.LogWarning("[CSK] Sanitized {Context}: '{Original}' -> '{Sanitized}'", context, value, sanitized);

            return sanitized;
        }

        #endregion

    }
}
