#nullable enable
using System.Globalization;
using System.Text.Json;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// An article's schema.org BlogPosting: only fields the repository holds - headline, description,
    /// date, keywords, image - with Nova Globen AB as author and publisher. No ratings, no invented fields. The page and
    /// image addresses are written root-relative and made absolute by the theme for the host being served.
    /// </summary>
    public static class ArticleJsonLd
    {
        public static string For(ArticleSummary article)
        {
            var organization = new { type = "Organization", name = "Nova Globen AB" };
            var data = new System.Collections.Generic.Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "BlogPosting",
                ["headline"] = article.Title,
                ["description"] = article.Summary,
                ["datePublished"] = article.PublishedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["inLanguage"] = "en",
                ["mainEntityOfPage"] = "{origin}/blog/" + article.Slug,
                ["author"] = new System.Collections.Generic.Dictionary<string, string> { ["@type"] = organization.type, ["name"] = organization.name, ["url"] = "{origin}/" },
                ["publisher"] = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["@type"] = organization.type,
                    ["name"] = organization.name,
                    ["url"] = "{origin}/",
                    ["logo"] = new System.Collections.Generic.Dictionary<string, string> { ["@type"] = "ImageObject", ["url"] = "{origin}/assets/logo-full.png" }
                }
            };
            if (article.Tags.Count > 0)
            {
                data["keywords"] = string.Join(", ", article.Tags);
            }
            if (article.CoverPng.Length > 0)
            {
                data["image"] = "{origin}" + article.CoverPng;
            }
            return JsonSerializer.Serialize(data);
        }
    }
}
