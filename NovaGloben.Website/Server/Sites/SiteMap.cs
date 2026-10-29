#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Microsoft.AspNetCore.Http;
using NovaGloben.Website.Articles;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// The sitemap: one per host, covering every site and language on it, built from the repository -
    /// the manifests' pages and the articles - so it lists exactly the public pages: no 404 page, no link-only menu entry,
    /// no Stomme admin or search page. A page with a counterpart in the other language carries hreflang alternates.
    /// </summary>
    /// <remarks>
    /// Stomme answers /sitemap.xml itself, by rewriting the request to /pages/sitemap.xml before any module middleware
    /// runs; but for a site in a folder it answers with the primary site's pages, and it lists its search page. This
    /// middleware therefore answers the rewritten path for every alias of the host. Measured 2026-10-23 on 10.69.0
    ///.
    /// </remarks>
    public static class SiteMap
    {
        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            if (!HttpMethods.IsGet(context.Request.Method) || !IsSitemapRequest(context.Request.Path.Value))
            {
                await next();
                return;
            }

            var host = context.Request.Host.Value ?? "";
            var scheme = host.StartsWith("localhost", StringComparison.OrdinalIgnoreCase) ? context.Request.Scheme : "https";
            context.Response.ContentType = "application/xml; charset=utf-8";
            await context.Response.WriteAsync(Write(scheme + "://" + host, SiteManifest.LoadAll(), ArticleSource.LoadAll(ArticleAssets.Exists)), Encoding.UTF8);
        }

        private static bool IsSitemapRequest(string? path) =>
            path != null && (path.EndsWith("/pages/sitemap.xml", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/sitemap.xml", StringComparison.OrdinalIgnoreCase));

        public static string Write(string origin, IReadOnlyList<SiteManifest> sites, IReadOnlyList<ArticleSource> articles)
        {
            const string ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
            const string xhtml = "http://www.w3.org/1999/xhtml";
            var builder = new StringBuilder();
            using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { Indent = true, Encoding = Encoding.UTF8 }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("urlset", ns);
                writer.WriteAttributeString("xmlns", "xhtml", null, xhtml);

                foreach (var site in sites)
                {
                    foreach (var page in site.Pages.Where(IsListed))
                    {
                        writer.WriteStartElement("url", ns);
                        writer.WriteElementString("loc", ns, origin + Url(site, page.Path));
                        foreach (var (language, url) in Alternates(site, page, sites))
                        {
                            writer.WriteStartElement("link", xhtml);
                            writer.WriteAttributeString("rel", "alternate");
                            writer.WriteAttributeString("hreflang", language);
                            writer.WriteAttributeString("href", origin + url);
                            writer.WriteEndElement();
                        }
                        writer.WriteEndElement();
                    }

                    if (site.Articles)
                    {
                        foreach (var article in articles.Where(a => !a.Draft))
                        {
                            writer.WriteStartElement("url", ns);
                            writer.WriteElementString("loc", ns, origin + Url(site, "blog/" + article.Slug));
                            writer.WriteElementString("lastmod", ns, article.PublishedOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                            writer.WriteEndElement();
                        }
                    }
                }

                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
        }

        private static bool IsListed(PageManifest page) =>
            page.Url.Length == 0 && !(page.Settings.TryGetValue(WebsiteKeys.NoIndex, out var noIndex) && noIndex == "true");

        /// <summary>A page's address on its site: the root keeps its slash ("/", "/sv/"), as the pages' canonical form.</summary>
        public static string Url(SiteManifest site, string path)
        {
            var root = site.Folder.Length == 0 ? "/" : "/" + site.Folder + "/";
            return path.Length == 0 ? root : root + path;
        }

        /// <summary>The page in every language it exists in (itself included), when it exists in more than one.</summary>
        private static IEnumerable<(string Language, string Url)> Alternates(SiteManifest site, PageManifest page, IReadOnlyList<SiteManifest> sites)
        {
            if (page.Settings.TryGetValue("NovaGloben:Counterpart", out var counterpart) && counterpart == "none")
            {
                yield break;
            }
            var versions = sites
                .Select(s => (Site: s, Page: s.Pages.FirstOrDefault(p => p.Path == page.Path && IsListed(p))))
                .Where(v => v.Page != null)
                .ToList();
            if (versions.Count < 2)
            {
                yield break;
            }
            foreach (var (other, _) in versions)
            {
                yield return (other.Culture, Url(other, page.Path));
            }
            var english = versions.FirstOrDefault(v => v.Site.Culture == "en");
            if (english.Site != null)
            {
                yield return ("x-default", Url(english.Site, page.Path));
            }
        }
    }
}
