#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// One article as the repository holds it: content/articles/&lt;slug&gt;/index.md, its front matter (title, date, slug,
    /// description, tags, cover, draft) and its markdown body. The files are compiled into the server assembly (see the
    /// Server project file); the covers beside them are served at /blog/&lt;slug&gt;/.
    /// </summary>
    public sealed record ArticleSource(
        string Slug,
        string Title,
        string Summary,
        IReadOnlyList<string> Tags,
        DateTime PublishedOn,
        string CoverPng,
        string CoverWebp,
        bool Draft,
        string Markdown,
        string Hash)
    {
        private const string Prefix = "content/articles/";

        /// <summary>
        /// Every article compiled into the server assembly, drafts included (the import skips them).
        /// <paramref name="webFileExists"/> answers whether a web-root path such as "blog/&lt;slug&gt;/cover.png" is served.
        /// </summary>
        public static IReadOnlyList<ArticleSource> LoadAll(Func<string, bool> webFileExists)
        {
            var assembly = typeof(ArticleSource).Assembly;
            return assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal) && name.EndsWith("/index.md", StringComparison.Ordinal))
                .Select(name =>
                {
                    using var stream = assembly.GetManifestResourceStream(name)!;
                    using var reader = new StreamReader(stream);
                    var folder = name.Substring(Prefix.Length, name.Length - Prefix.Length - "/index.md".Length);
                    return Parse(folder, reader.ReadToEnd(), webFileExists);
                })
                .OrderByDescending(article => article.PublishedOn)
                .ThenBy(article => article.Slug, StringComparer.Ordinal)
                .ToList();
        }
        /// <summary>Parses one index.md (<paramref name="folder"/> is its folder under content/articles).</summary>
        public static ArticleSource Parse(string folder, string text, Func<string, bool> webFileExists)
        {
            var (front, body) = SplitFrontMatter(text);
            var slug = (Value(front, "slug") ?? folder).Trim().ToLowerInvariant();
            if (!DateTime.TryParse(Value(front, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var date))
            {
                throw new InvalidDataException($"Article '{folder}' has no valid date in its front matter.");
            }
            var title = Value(front, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                throw new InvalidDataException($"Article '{folder}' has no title in its front matter.");
            }

            var hasCover = !string.IsNullOrWhiteSpace(Value(front, "cover")) && webFileExists($"blog/{folder}/cover.png");
            var png = hasCover ? $"/blog/{slug}/cover.png" : "";
            var webp = hasCover && webFileExists($"blog/{folder}/cover.webp") ? $"/blog/{slug}/cover.webp" : "";
            var draft = string.Equals(Value(front, "draft"), "true", StringComparison.OrdinalIgnoreCase);

            return new ArticleSource(
                slug,
                title.Trim(),
                (Value(front, "description") ?? "").Trim(),
                ParseTags(Value(front, "tags")),
                DateTime.SpecifyKind(date.Date, DateTimeKind.Utc),
                png,
                webp,
                draft,
                body,
                ComputeHash(text));
        }

        private static (Dictionary<string, string> Front, string Body) SplitFrontMatter(string text)
        {
            var front = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            text = text.TrimStart('﻿').Replace("\r\n", "\n").Replace('\r', '\n');
            if (!text.StartsWith("---", StringComparison.Ordinal))
            {
                return (front, text);
            }

            var lines = text.Split('\n');
            for (var i = 1; i < lines.Length; i++)
            {
                if (lines[i].Trim() == "---")
                {
                    return (front, string.Join('\n', lines.Skip(i + 1)).TrimStart('\n'));
                }
                var colon = lines[i].IndexOf(':');
                if (colon > 0)
                {
                    front[lines[i][..colon].Trim()] = lines[i][(colon + 1)..].Trim().Trim('"');
                }
            }
            return (front, text);
        }

        private static string? Value(Dictionary<string, string> front, string key) =>
            front.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

        private static IReadOnlyList<string> ParseTags(string? raw)
        {
            raw = (raw ?? "").Trim().TrimStart('[').TrimEnd(']');
            return raw.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(tag => tag.Trim().Trim('"', '\'').ToLowerInvariant())
                .Where(tag => tag.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        private static string ComputeHash(string text) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ArticleRenderer.Version + "\n" + text)));
    }
}
