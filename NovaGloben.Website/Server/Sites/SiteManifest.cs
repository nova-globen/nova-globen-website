#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using NovaGloben.Website.Content;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// A site as the repository declares it: content/sites/&lt;key&gt;/site.json. The site's reconciliation (SiteReconciler)
    /// makes the site match it on every boot - its settings, its pages and the sections on them.
    /// </summary>
    public sealed class SiteManifest
    {
        /// <summary>The site's key, also its folder under content/sites and the value of its NovaGloben:SiteKey setting.</summary>
        public string Key { get; set; } = "";

        public string Name { get; set; } = "";

        /// <summary>The site's culture: the html element's lang, and the theme's language.</summary>
        public string Culture { get; set; } = "en";

        /// <summary>
        /// Empty for the primary site (it keeps the aliases the installation gave it). For a site in a folder of the primary
        /// site's hosts, the folder: "sv" serves the site at every primary alias + "/sv".
        /// </summary>
        public string Folder { get; set; } = "";

        /// <summary>True on the site whose articles are imported from content/articles.</summary>
        public bool Articles { get; set; }

        /// <summary>Site settings, applied on every boot (the theme's NovaGloben:* options among them).</summary>
        public Dictionary<string, string> Settings { get; set; } = new();

        public List<PageManifest> Pages { get; set; } = new();

        private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

        /// <summary>Every site.json compiled into the Shared assembly, the primary site (no folder) first.</summary>
        public static IReadOnlyList<SiteManifest> LoadAll() =>
            ContentLibrary.Keys()
                .Where(key => key.EndsWith("/site.json", StringComparison.Ordinal))
                .Select(key => JsonSerializer.Deserialize<SiteManifest>(ContentLibrary.Get(key)!, Json)
                    ?? throw new InvalidOperationException($"content/sites/{key} is empty."))
                .OrderBy(manifest => manifest.Folder.Length > 0)
                .ThenBy(manifest => manifest.Key, StringComparer.Ordinal)
                .ToList();
    }

    public sealed class PageManifest
    {
        /// <summary>The page's path below the site ("" is the home page). Its parent is the path before the last "/".</summary>
        public string Path { get; set; } = "";

        /// <summary>The name in the menu and the footer.</summary>
        public string Name { get; set; } = "";

        /// <summary>The browser title, in full.</summary>
        public string Title { get; set; } = "";

        /// <summary>The meta description, also Open Graph's.</summary>
        public string Description { get; set; } = "";

        public bool Navigation { get; set; }

        public int Order { get; set; } = 1;

        /// <summary>A link instead of a page (the page then holds no sections): a root-relative or absolute URL.</summary>
        public string Url { get; set; } = "";

        /// <summary>Page settings, beside the ones made from the fields above (the theme's NovaGloben:Counterpart among them).</summary>
        public Dictionary<string, string> Settings { get; set; } = new();

        public List<SectionManifest> Sections { get; set; } = new();
    }

    /// <summary>One section of a page: a module instance. Exactly one of Content, Articles and Contact is set.</summary>
    public sealed class SectionManifest
    {
        /// <summary>Unique on its page: the module's title, which the reconciliation matches it by. Never shown.</summary>
        public string Name { get; set; } = "";

        /// <summary>A page-content file below the site's folder, for example "pages/home-hero.html".</summary>
        public string Content { get; set; } = "";

        /// <summary>An article view: "list" (paged, filtered by tag), "latest" (the newest few) or "article" (one).</summary>
        public string Articles { get; set; } = "";

        /// <summary>The number of articles: per list page, or in "latest".</summary>
        public int Count { get; set; }

        /// <summary>"form": the contact form (Client/Modules/Contact).</summary>
        public string Contact { get; set; } = "";
    }
}
