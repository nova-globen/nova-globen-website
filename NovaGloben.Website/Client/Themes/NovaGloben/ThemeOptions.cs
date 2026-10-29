using System;
using System.Linq;

namespace NovaGloben.Website.Client.Themes.NovaGloben
{
    /// <summary>
    /// What the theme reads from the site's and the page's settings (a page setting wins over the site's). The site
    /// templates set them; every one has a default, so a site without them still renders.
    /// <list type="bullet">
    /// <item><c>NovaGloben:CounterpartBase</c> (site): where the other language's site starts, for example <c>/sv/</c> on
    /// the English site and <c>/</c> on the Swedish one. Empty: no language link.</item>
    /// <item><c>NovaGloben:Counterpart</c> (page): this page's counterpart URL when it is not the same path on the other
    /// site; <c>none</c> when it has none.</item>
    /// <item><c>NovaGloben:CtaPath</c>, <c>NovaGloben:CtaLabel</c>: the header button's page and wording.</item>
    /// <item><c>NovaGloben:ServicesPath</c>: the page whose child pages the footer lists as services.</item>
    /// <item><c>NovaGloben:FooterPaths</c>: comma-separated page paths for the footer's company column.</item>
    /// </list>
    /// </summary>
    public sealed class ThemeOptions
    {
        public const string Prefix = "NovaGloben:";

        public string CounterpartBase { get; private init; }
        public string Counterpart { get; private init; }
        public string CtaPath { get; private init; }
        public string CtaLabel { get; private init; }
        public string ServicesPath { get; private init; }
        public string[] FooterPaths { get; private init; }

        public static ThemeOptions From(Func<string, string> setting)
        {
            string Get(string key, string fallback) => setting(Prefix + key) is { Length: > 0 } value ? value : fallback;

            return new ThemeOptions
            {
                CounterpartBase = Get("CounterpartBase", ""),
                Counterpart = Get("Counterpart", ""),
                CtaPath = Get("CtaPath", "contact"),
                CtaLabel = Get("CtaLabel", ""),
                ServicesPath = Get("ServicesPath", "services"),
                FooterPaths = Get("FooterPaths", "about,contact,privacy,terms,cookies")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToArray()
            };
        }

        /// <summary>The URL of this page in the other language, or null when there is none.</summary>
        public string CounterpartUrl(string pagePath)
        {
            if (Counterpart.Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
            if (Counterpart.Length > 0)
            {
                return Counterpart;
            }
            if (CounterpartBase.Length == 0)
            {
                return null;
            }
            return CounterpartBase.TrimEnd('/') + "/" + (pagePath ?? "").TrimStart('/');
        }
    }
}
