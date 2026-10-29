using NovaGloben.Website.Articles;
using Xunit;

namespace NovaGloben.Website.Tests;

public class ArticleTests
{
    [Fact]
    public void All_33_articles_parse_with_their_folder_as_slug()
    {
        var root = Path.Combine(ContentTests.RepositoryRoot(), "content", "articles");
        var articles = ArticleSource.LoadAll(path => File.Exists(Path.Combine(root, path["blog/".Length..].Replace('/', Path.DirectorySeparatorChar))));

        Assert.Equal(33, articles.Count);
        Assert.All(articles, a =>
        {
            Assert.False(a.Draft);
            Assert.False(string.IsNullOrWhiteSpace(a.Title));
            Assert.False(string.IsNullOrWhiteSpace(a.Summary));
            Assert.Matches("^[a-z0-9-]+$", a.Slug);
            Assert.True(Directory.Exists(Path.Combine(root, a.Slug)), a.Slug);
            Assert.InRange(a.PublishedOn, new DateTime(2025, 1, 1), new DateTime(2026, 12, 31));
        });
        // One article had no cover on the old site, and has none here.
        Assert.Equal(32, articles.Count(a => a.CoverPng.Length > 0));
        Assert.Equal(new DateTime(2025, 9, 25), articles.Min(a => a.PublishedOn));
        Assert.Equal(new DateTime(2026, 7, 13), articles.Max(a => a.PublishedOn));
    }

    [Fact]
    public void Every_webp_cover_has_its_phone_variant()
    {
        var root = Path.Combine(ContentTests.RepositoryRoot(), "content", "articles");
        var missing = Directory.GetFiles(root, "cover.webp", SearchOption.AllDirectories)
            .Where(f => !File.Exists(Path.Combine(Path.GetDirectoryName(f)!, "cover-800.webp")))
            .Select(f => Path.GetFileName(Path.GetDirectoryName(f)))
            .ToList();

        Assert.True(missing.Count == 0, "Run scripts/content/cover-variants.ps1 for: " + string.Join(", ", missing));
    }

    [Theory]
    [InlineData("/blog/s/cover.webp", "/blog/s/cover-800.webp 800w, /blog/s/cover.webp 1200w")]
    [InlineData("/blog/s/other.webp", "/blog/s/other.webp")]
    public void The_cover_srcset_offers_the_phone_variant(string webp, string srcset) =>
        Assert.Equal(srcset, NovaGloben.Website.Client.Modules.Articles.Index.CoverSrcset(webp));

    [Fact]
    public void The_front_matter_is_read()
    {
        var source = ArticleSource.Parse("folder", "---\ntitle: \"A title\"\ndate: 2025-09-26\nslug: My-Slug\ndescription: \"Summary\"\ntags: [Versioning, rest]\ncover: ./cover.png\ndraft: false\n---\n\n# A title\n\nBody", p => p == "blog/folder/cover.png");

        Assert.Equal("my-slug", source.Slug);
        Assert.Equal("A title", source.Title);
        Assert.Equal("Summary", source.Summary);
        Assert.Equal(new[] { "versioning", "rest" }, source.Tags);
        Assert.Equal(new DateTime(2025, 9, 26, 0, 0, 0, DateTimeKind.Utc), source.PublishedOn);
        Assert.Equal("/blog/my-slug/cover.png", source.CoverPng);
        Assert.Equal("", source.CoverWebp);
        Assert.StartsWith("# A title", source.Markdown);
    }

    [Fact]
    public void The_leading_title_is_dropped_and_other_level_one_headings_become_level_two()
    {
        var html = ArticleRenderer.Render("# Title\n\nIntro\n\n# Another\n\n## Section");

        Assert.DoesNotContain("<h1", html);
        Assert.Contains("<h2 id=\"another\">Another</h2>", html);
        Assert.Contains("<h2 id=\"section\">Section</h2>", html);
    }

    [Fact]
    public void Code_takes_focus_and_csharp_is_highlighted_on_the_server()
    {
        var html = ArticleRenderer.Render("```csharp\npublic class A { } // note\n```\n\n```yaml\nkey: <value>\n```");

        Assert.Contains("<pre class=\"ng-code\" tabindex=\"0\" data-lang=\"csharp\"><code class=\"language-csharp\">", html);
        Assert.Contains("<span class=\"keyword\">public</span>", html);
        Assert.Contains("<span class=\"comment\">", html);
        // A language ColorCode does not know is written escaped and plain.
        Assert.Contains("key: &lt;value&gt;", html);
        Assert.DoesNotContain("<script", html);
    }

    [Fact]
    public void The_blog_posting_json_ld_holds_only_the_articles_facts()
    {
        var json = ArticleJsonLd.For(new ArticleSummary { Slug = "s", Title = "T", Summary = "D", Tags = { "a", "b" }, PublishedOn = new DateTime(2025, 9, 26), CoverPng = "/blog/s/cover.png" });
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("BlogPosting", root.GetProperty("@type").GetString());
        Assert.Equal("2025-09-26", root.GetProperty("datePublished").GetString());
        Assert.Equal("{origin}/blog/s/cover.png", root.GetProperty("image").GetString());
        Assert.Equal("Nova Globen AB", root.GetProperty("author").GetProperty("name").GetString());
        Assert.False(root.TryGetProperty("aggregateRating", out _));
    }

    [Fact]
    public void The_feed_is_well_formed_atom()
    {
        var xml = ArticleFeed.Write("https://example.test", new[] { new ArticleSummary { Slug = "s", Title = "T & U", Summary = "D", PublishedOn = new DateTime(2025, 9, 26) } });
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace atom = "http://www.w3.org/2005/Atom";

        Assert.Equal("T & U", doc.Root!.Element(atom + "entry")!.Element(atom + "title")!.Value);
        Assert.Equal("https://example.test/blog/s", doc.Root.Element(atom + "entry")!.Element(atom + "id")!.Value);
    }
}
