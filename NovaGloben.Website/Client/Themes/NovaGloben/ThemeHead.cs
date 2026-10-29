using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NovaGloben.Website.Client.Themes.NovaGloben
{
    /// <summary>
    /// The head tags of a page: description, canonical, hreflang pairs, Open Graph and Twitter
    /// card, the article feed's link, and JSON-LD (the company's Organization and the WebSite on a home page; the page's
    /// own, an article's BlogPosting, where the site reconciliation stored one). Absolute addresses use the origin the
    /// request came to, so every host names itself. The Organization holds only verified company facts.
    /// </summary>
    public static class ThemeHead
    {
        public sealed record Input(
            string Origin,
            string CanonicalPath,
            string Title,
            string Description,
            string Language,
            string CounterpartPath,
            string CounterpartLanguage,
            string OgType,
            string OgImagePath,
            string JsonLd,
            bool IsHome,
            bool NoIndex,
            bool HasArticleFeed);

        /// <summary>The Latin upright Inter file theme.css names first; the preload must name the same URL to be used.</summary>
        public const string FontPath = "/Themes/NovaGloben.Website.Client.Themes.NovaGloben/fonts/inter/inter-latin-wght-normal.woff2";

        public static string Build(Input input)
        {
            var head = new StringBuilder();
            var canonical = input.Origin + input.CanonicalPath;

            // The text font, fetched with the page instead of after the stylesheet, so the text does not reflow when it
            // arrives (Lighthouse measured the swap as layout shift). Only the Latin upright file every page uses.
            head.Append($"<link rel=\"preload\" href=\"{FontPath}\" as=\"font\" type=\"font/woff2\" crossorigin />\n");

            if (input.Description.Length > 0)
            {
                Meta(head, "name", "description", input.Description);
            }
            if (input.NoIndex)
            {
                Meta(head, "name", "robots", "noindex");
            }
            else
            {
                head.Append($"<link rel=\"canonical\" href=\"{Attr(canonical)}\" />\n");
            }

            if (!input.NoIndex && !string.IsNullOrEmpty(input.CounterpartPath))
            {
                var other = input.Origin + input.CounterpartPath;
                head.Append($"<link rel=\"alternate\" hreflang=\"{input.Language}\" href=\"{Attr(canonical)}\" />\n");
                head.Append($"<link rel=\"alternate\" hreflang=\"{input.CounterpartLanguage}\" href=\"{Attr(other)}\" />\n");
                var english = input.Language == "en" ? canonical : other;
                head.Append($"<link rel=\"alternate\" hreflang=\"x-default\" href=\"{Attr(english)}\" />\n");
            }

            if (input.HasArticleFeed)
            {
                head.Append($"<link rel=\"alternate\" type=\"application/atom+xml\" title=\"Nova Globen AB - Insights\" href=\"{Attr(input.Origin + "/blog/feed.xml")}\" />\n");
            }

            var image = input.Origin + (string.IsNullOrEmpty(input.OgImagePath) ? "/assets/og-image.png" : input.OgImagePath);
            Meta(head, "property", "og:site_name", "Nova Globen AB");
            Meta(head, "property", "og:type", string.IsNullOrEmpty(input.OgType) ? "website" : input.OgType);
            Meta(head, "property", "og:title", input.Title);
            if (input.Description.Length > 0)
            {
                Meta(head, "property", "og:description", input.Description);
            }
            Meta(head, "property", "og:url", canonical);
            Meta(head, "property", "og:image", image);
            Meta(head, "property", "og:locale", input.Language == "sv" ? "sv_SE" : "en_US");
            Meta(head, "name", "twitter:card", "summary_large_image");

            if (input.IsHome)
            {
                JsonLd(head, Organization(input.Origin));
                JsonLd(head, JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["@context"] = "https://schema.org",
                    ["@type"] = "WebSite",
                    ["name"] = "Nova Globen AB",
                    ["url"] = canonical,
                    ["inLanguage"] = input.Language
                }));
            }
            if (!string.IsNullOrEmpty(input.JsonLd))
            {
                JsonLd(head, input.JsonLd.Replace("{origin}", input.Origin));
            }
            return head.ToString();
        }

        /// <summary>The company, from its verified facts only.</summary>
        public static string Organization(string origin) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Organization",
            ["name"] = "Nova Globen AB",
            ["legalName"] = "Nova Globen AB",
            ["url"] = origin + "/",
            ["logo"] = origin + "/assets/logo-full.png",
            ["email"] = "info@nova-globen.se",
            ["vatID"] = "SE559481693501",
            ["identifier"] = new Dictionary<string, string> { ["@type"] = "PropertyValue", ["propertyID"] = "Organisationsnummer", ["value"] = "559481-6935" },
            ["address"] = new Dictionary<string, string>
            {
                ["@type"] = "PostalAddress",
                ["streetAddress"] = "Mössebergsvägen 20, LGH 1002",
                ["postalCode"] = "167 43",
                ["addressLocality"] = "Bromma",
                ["addressCountry"] = "SE"
            }
        });

        private static void Meta(StringBuilder head, string attribute, string name, string content) =>
            head.Append($"<meta {attribute}=\"{name}\" content=\"{Attr(content)}\" />\n");

        // Inside <script>, "</" would end the element early; JSON allows "<\/" for it.
        private static void JsonLd(StringBuilder head, string json) =>
            head.Append("<script type=\"application/ld+json\">").Append(json.Replace("</", "<\\/")).Append("</script>\n");

        private static string Attr(string value) => WebUtility.HtmlEncode(value ?? "");
    }
}
