using System.Text.RegularExpressions;
using NovaGloben.Website.Articles;
using NovaGloben.Website.Content;
using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>
/// The repository's content holds together: every section a manifest names exists, the two languages declare the same
/// pages, and every internal link in a page or an article leads to a page, an article or a file the site serves - the
/// link check of the content rules.
/// </summary>
public class ContentTests
{
    private static readonly IReadOnlyList<SiteManifest> Sites = SiteManifest.LoadAll();
    private static readonly IReadOnlyList<ArticleSource> Articles = ArticleSource.LoadAll(_ => true);

    [Fact]
    public void Both_company_sites_are_declared()
    {
        Assert.Contains(Sites, s => s.Key == "company-en" && s.Folder == "" && s.Culture == "en" && s.Articles);
        Assert.Contains(Sites, s => s.Key == "company-sv" && s.Folder == "sv" && s.Culture == "sv" && !s.Articles);
    }

    [Fact]
    public void Every_section_names_a_content_file_or_an_article_view()
    {
        foreach (var site in Sites)
        {
            foreach (var page in site.Pages)
            {
                Assert.True(page.Url.Length > 0 || page.Sections.Count > 0, $"{site.Key}/{page.Path} has no section");
                Assert.Equal(page.Sections.Count, page.Sections.Select(s => s.Name).Distinct().Count());
                foreach (var section in page.Sections)
                {
                    if (section.Contact.Length > 0)
                    {
                        Assert.Equal("form", section.Contact);
                        Assert.Equal("contact", page.Path);
                    }
                    else if (section.Articles.Length > 0)
                    {
                        Assert.Contains(section.Articles, new[] { "list", "latest" });
                    }
                    else
                    {
                        Assert.NotNull(ContentLibrary.Get(site.Key + "/" + section.Content));
                    }
                }
            }
        }
    }

    [Fact]
    public void Every_page_has_a_title_and_all_but_the_404_a_description()
    {
        foreach (var page in Sites.SelectMany(s => s.Pages))
        {
            Assert.False(string.IsNullOrWhiteSpace(page.Title), page.Path);
            if (page.Path != "404" && page.Url.Length == 0)
            {
                Assert.InRange(page.Description.Length, 40, 170);
            }
        }
    }

    [Fact]
    public void Every_page_with_content_has_exactly_one_h1()
    {
        foreach (var site in Sites)
        {
            foreach (var page in site.Pages.Where(p => p.Url.Length == 0))
            {
                var html = string.Concat(page.Sections.Where(s => s.Content.Length > 0).Select(s => ContentLibrary.Get(site.Key + "/" + s.Content)));
                Assert.True(Regex.Matches(html, "<h1[ >]").Count == 1, $"{site.Key}/{page.Path} has {Regex.Matches(html, "<h1[ >]").Count} h1");
            }
        }
    }

    [Fact]
    public void The_languages_declare_the_same_pages_except_the_articles()
    {
        var english = Sites.Single(s => s.Key == "company-en").Pages.Where(p => p.Url.Length == 0).Select(p => p.Path).ToHashSet();
        var swedish = Sites.Single(s => s.Key == "company-sv").Pages.Where(p => p.Url.Length == 0).Select(p => p.Path).ToHashSet();
        english.Remove("blog");
        Assert.Equal(english.OrderBy(p => p), swedish.OrderBy(p => p));
    }

    public static IEnumerable<object[]> ContentFiles() =>
        ContentLibrary.Keys().Where(k => k.EndsWith(".html", StringComparison.Ordinal)).Select(k => new object[] { k });

    [Theory]
    [MemberData(nameof(ContentFiles))]
    public void Every_internal_link_in_a_page_leads_somewhere(string key)
    {
        foreach (Match match in Regex.Matches(ContentLibrary.Get(key)!, "(?:href|src)=\"([^\"]+)\""))
        {
            var target = match.Groups[1].Value;
            Assert.True(Resolves(target), $"{key}: {target} leads nowhere");
        }
    }

    [Fact]
    public void Every_internal_link_in_an_article_leads_somewhere()
    {
        foreach (var article in Articles)
        {
            var html = ArticleRenderer.Render(article.Markdown);
            foreach (Match match in Regex.Matches(html, "<a [^>]*href=\"([^\"]+)\""))
            {
                var target = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value);
                Assert.True(target.StartsWith('#') || Resolves(target), $"{article.Slug}: {target} leads nowhere");
            }
        }
    }

    [Fact]
    public void No_page_names_a_phone_number()
    {
        foreach (var key in ContentLibrary.Keys())
        {
            // Any Swedish mobile number, in the national or the international form: the site publishes none.
            Assert.DoesNotMatch(@"(?<!\d)07[02369][- ]?\d{3}[- ]?\d{2}[- ]?\d{2}(?!\d)|\+46[\s-]?7\d", ContentLibrary.Get(key)!);
        }
    }

    /// <summary>Whether an address in the content resolves on the sites as the repository declares them.</summary>
    private static bool Resolves(string target)
    {
        if (target.StartsWith("mailto:info@nova-globen.se", StringComparison.Ordinal) || target.StartsWith("https://", StringComparison.Ordinal) || target.StartsWith("http://", StringComparison.Ordinal))
        {
            return true;
        }
        if (!target.StartsWith('/'))
        {
            return false;
        }

        var path = target.Split('?', '#')[0];
        if (path.StartsWith("/assets/", StringComparison.Ordinal))
        {
            return File.Exists(Path.Combine(RepositoryRoot(), "NovaGloben.Website", "Server", "wwwroot", path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));
        }
        if (path == "/blog/feed.xml")
        {
            return true;
        }
        var product = NovaGloben.Website.Sites.ProductLinks.Target(path);
        if (product != null)
        {
            // A product's stable address (ProductLinks): it leads where its redirect leads.
            return Resolves(product);
        }
        if (path.StartsWith("/blog/", StringComparison.Ordinal))
        {
            var rest = path["/blog/".Length..].TrimEnd('/');
            return Articles.Any(a => a.Slug == rest) || ArticleAssetExists(rest);
        }

        foreach (var site in Sites.OrderByDescending(s => s.Folder.Length))
        {
            var root = site.Folder.Length == 0 ? "/" : "/" + site.Folder + "/";
            if (path == root.TrimEnd('/') && site.Folder.Length > 0)
            {
                return true;
            }
            if (path.StartsWith(root, StringComparison.Ordinal))
            {
                var pagePath = path[root.Length..].TrimEnd('/');
                return site.Pages.Any(p => p.Path == pagePath);
            }
        }
        return false;
    }

    private static bool ArticleAssetExists(string slugAndFile) =>
        File.Exists(Path.Combine(RepositoryRoot(), "content", "articles", slugAndFile.Replace('/', Path.DirectorySeparatorChar)));

    internal static string RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content", "sites")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? throw new InvalidOperationException("The repository root (with content/sites) was not found above the test's folder.");
    }
}
