using System;
using System.Globalization;
using System.Text;

namespace Didin.Module.Media.Services
{
    /// <summary>
    /// Search folding: both the catalog text and the query are reduced to one comparable form, so a search
    /// finds a title however it was typed.
    /// <list type="bullet">
    /// <item>Persian: Arabic yeh/alef maksura and kaf become Persian yeh/keheh, teh marbuta becomes heh, hamza and
    /// madda forms fold to their base letter, ZWNJ, tatweel and harakat disappear, Persian/Arabic-Indic digits
    /// become 0-9 ("فصل 2" finds "فصل ۲").</item>
    /// <item>Latin/Turkish: accents fold away and dotless i is i ("isik" finds "Işık").</item>
    /// <item>Punctuation is a space, case is ignored, and a match may ignore spaces ("spiderman" finds "Spider-Man").</item>
    /// </list>
    /// </summary>
    public static class SearchText
    {
        public static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(decomposed.Length);
            foreach (var original in decomposed)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(original);
                if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.Format) continue; // accents, harakat, ZWNJ
                var c = original switch
                {
                    'ي' or 'ى' => 'ی', // Arabic yeh, alef maksura -> Persian yeh
                    'ك' => 'ک',             // Arabic kaf -> keheh
                    'ة' => 'ه',             // teh marbuta -> heh
                    'ٱ' => 'ا',             // alef wasla -> alef
                    'ı' => 'i',                  // Turkish dotless i
                    _ => original
                };
                if (c == 'ـ') continue;                                                  // tatweel
                if (c >= '۰' && c <= '۹') c = (char)('0' + (c - '۰'));        // Persian digits
                else if (c >= '٠' && c <= '٩') c = (char)('0' + (c - '٠'));   // Arabic-Indic digits
                sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
            }
            return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        /// <summary>Both arguments already normalized. Matches with spaces, or with all spaces removed.</summary>
        public static bool Contains(string haystack, string needle) =>
            haystack.Contains(needle, StringComparison.Ordinal)
            || haystack.Replace(" ", "").Contains(needle.Replace(" ", ""), StringComparison.Ordinal);
    }
}
