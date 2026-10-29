using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Didin.Shared.Models
{
    /// <summary>
    /// Subtitle text handling shared by the catalogue import (language detection) and the server (WebVTT for the player).
    /// The library's subtitles are SubRip files in UTF-8, UTF-16 or a legacy single-byte code page -
    /// Persian (Windows-1256), Turkish (Windows-1254) or Western (Windows-1252).
    /// </summary>
    public static class SubtitleText
    {
        private static readonly Regex Timing = new(
            @"^(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})\s*-->\s*(\d{1,2}):(\d{2}):(\d{2})[,.](\d{1,3})(.*)$",
            RegexOptions.Compiled);
        private static readonly Regex AssTags = new(@"\{\\[^}]*\}", RegexOptions.Compiled);

        static SubtitleText() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        public static string Decode(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE) return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                // a legacy code page - pick the one whose letters make sense
            }

            var persian = Encoding.GetEncoding(1256).GetString(bytes);
            var nonAscii = persian.Count(c => c > 127 && char.IsLetter(c));
            if (nonAscii > 0 && persian.Count(IsArabicScript) * 2 > nonAscii)
            {
                return persian;
            }
            var turkish = Encoding.GetEncoding(1254).GetString(bytes);
            return turkish.Count(c => "ğĞşŞıİ".IndexOf(c) >= 0) >= 3 ? turkish : Encoding.GetEncoding(1252).GetString(bytes);
        }

        /// <summary>ISO 639-1 code of the subtitle's language: "fa" (Arabic script), "tr", else "en".</summary>
        public static string DetectLanguage(string text)
        {
            var letters = text.Count(char.IsLetter);
            if (letters == 0) return "und";
            if (text.Count(IsArabicScript) * 10 > letters * 3) return "fa";
            if (text.Count(c => "ğĞşŞıİ".IndexOf(c) >= 0) >= 5) return "tr";
            return "en";
        }

        public static string LanguageName(string lang) => lang switch
        {
            "fa" => "Persian",
            "tr" => "Turkish",
            "en" => "English",
            "ar" => "Arabic",
            "fr" => "French",
            "de" => "German",
            "es" => "Spanish",
            "it" => "Italian",
            "ru" => "Russian",
            "pt" => "Portuguese",
            _ => "Subtitles"
        };

        /// <summary>SubRip (or WebVTT) text as WebVTT: header, '.' millisecond separators, ASS override tags removed.</summary>
        public static string ToWebVtt(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n').TrimStart('﻿');
            if (text.StartsWith("WEBVTT", StringComparison.Ordinal)) return text;

            var sb = new StringBuilder("WEBVTT\n\n", text.Length + 64);
            foreach (var line in text.Split('\n'))
            {
                var m = Timing.Match(line.Trim());
                if (m.Success)
                {
                    sb.Append(Stamp(m, 1)).Append(" --> ").Append(Stamp(m, 5)).Append('\n');
                }
                else
                {
                    sb.Append(AssTags.Replace(line, "")).Append('\n');
                }
            }
            return sb.ToString();
        }

        private static string Stamp(Match m, int g) =>
            $"{int.Parse(m.Groups[g].Value):00}:{m.Groups[g + 1].Value}:{m.Groups[g + 2].Value}.{m.Groups[g + 3].Value.PadRight(3, '0')}";

        private static bool IsArabicScript(char c) =>
            (c >= '؀' && c <= 'ۿ') || (c >= 'ﭐ' && c <= '﷿') || (c >= 'ﹰ' && c <= '﻿');
    }
}
