using NovaGloben.Website.Articles;
using NovaGloben.Website.Client.Themes.NovaGloben;
using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>The old addresses, the language links, the sitemap and the head tags.</summary>
public class AddressTests
{
    private static readonly string[] Folders = { "sv" };

    [Theory]
    [InlineData("/privacy.html", "/privacy")]
    [InlineData("/terms.html", "/terms")]
    [InlineData("/sv/privacy.html", "/sv/privacy")]
    [InlineData("/sv/terms.html", "/sv/terms")]
    [InlineData("/index.html", "/")]
    [InlineData("/sv/index.html", "/sv/")]
    [InlineData("/blog/", "/blog")]
    [InlineData("/blog/index.html", "/blog")]
    [InlineData("/blog/versioning-rest-api/", "/blog/versioning-rest-api")]
    [InlineData("/blog/versioning-rest-api/index.html", "/blog/versioning-rest-api")]
    [InlineData("/services/", "/services")]
    [InlineData("/services/net-development", "/services/architecture")]
    [InlineData("/services/azure-cloud/", "/services/architecture")]
    [InlineData("/services/technical-consulting", "/services/architecture")]
    [InlineData("/services/ai-rag", "/services/ai")]
    [InlineData("/sv/services/ai-rag", "/sv/services/ai")]
    [InlineData("/sv/services/net-development", "/sv/services/architecture")]
    [InlineData("/core", "/products/stomme")]
    [InlineData("/deck", "/products/fravashi")]
    [InlineData("/sv/core", "/sv/products/stomme")]
    [InlineData("/sv/deck/", "/sv/products/fravashi")]
    [InlineData("/vacha", "/products/vacha")]
    [InlineData("/vacha/download", "/products/vacha/download")]
    [InlineData("/vacha/pricing/", "/products/vacha/pricing")]
    [InlineData("/sv/vacha", "/sv/products/vacha")]
    [InlineData("/tond", "/products/vacha")]
    [InlineData("/tond/download", "/products/vacha/download")]
    [InlineData("/tond/pricing", "/products/vacha/pricing")]
    [InlineData("/sv/tond", "/sv/products/vacha")]
    [InlineData("/sv/tond/download/", "/sv/products/vacha/download")]
    public void An_old_address_has_one_redirect_to_its_new_home(string path, string expected)
    {
        Assert.Equal(expected, LegacyUrls.Target(path, Folders));
        Assert.Null(LegacyUrls.Target(expected, Folders)); // never a chain
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/sv/")]
    [InlineData("/blog")]
    [InlineData("/blog/versioning-rest-api")]
    [InlineData("/blog/versioning-rest-api/cover.png")]
    [InlineData("/sv/services")]
    [InlineData("/products/stomme")]
    [InlineData("/sv/products/vacha/download")]
    public void A_current_address_is_left_alone(string path) => Assert.Null(LegacyUrls.Target(path, Folders));

    [Theory]
    [InlineData("/sv/", "", "", "/sv/")]
    [InlineData("/sv/", "", "services", "/sv/services")]
    [InlineData("/", "", "services/ai", "/services/ai")]
    [InlineData("/sv/", "none", "blog", null)]
    [InlineData("", "", "services", null)]
    public void The_language_link_points_at_the_counterpart(string counterpartBase, string counterpart, string pagePath, string? expected)
    {
        var options = ThemeOptions.From(key => key switch
        {
            "NovaGloben:CounterpartBase" => counterpartBase,
            "NovaGloben:Counterpart" => counterpart,
            _ => null!
        });
        Assert.Equal(expected, options.CounterpartUrl(pagePath));
    }

    [Fact]
    public void The_sitemap_lists_the_public_pages_with_their_languages()
    {
        var xml = SiteMap.Write("https://example.test", SiteManifest.LoadAll(), ArticleSource.LoadAll(_ => false));
        var doc = System.Xml.Linq.XDocument.Parse(xml);
        System.Xml.Linq.XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        System.Xml.Linq.XNamespace xhtml = "http://www.w3.org/1999/xhtml";
        var urls = doc.Root!.Elements(ns + "url").ToList();
        var locs = urls.Select(u => u.Element(ns + "loc")!.Value).ToList();

        Assert.Contains("https://example.test/", locs);
        Assert.Contains("https://example.test/sv/", locs);
        Assert.Contains("https://example.test/blog/versioning-rest-api", locs);
        Assert.DoesNotContain("https://example.test/404", locs);
        Assert.DoesNotContain("https://example.test/sv/404", locs);
        Assert.DoesNotContain("https://example.test/sv/insights", locs);
        Assert.Equal(locs.Count, locs.Distinct().Count());

        var services = urls.Single(u => u.Element(ns + "loc")!.Value == "https://example.test/services");
        var alternates = services.Elements(xhtml + "link").ToDictionary(l => l.Attribute("hreflang")!.Value, l => l.Attribute("href")!.Value);
        Assert.Equal("https://example.test/sv/services", alternates["sv"]);
        Assert.Equal("https://example.test/services", alternates["x-default"]);
    }

    [Fact]
    public void The_head_has_canonical_hreflang_and_the_companys_facts()
    {
        var head = ThemeHead.Build(new ThemeHead.Input(
            Origin: "https://nova-globen.se", CanonicalPath: "/", Title: "T", Description: "D", Language: "en",
            CounterpartPath: "/sv/", CounterpartLanguage: "sv", OgType: "website", OgImagePath: "", JsonLd: "",
            IsHome: true, NoIndex: false, HasArticleFeed: true));

        Assert.Contains("<link rel=\"canonical\" href=\"https://nova-globen.se/\" />", head);
        Assert.Contains("hreflang=\"sv\" href=\"https://nova-globen.se/sv/\"", head);
        Assert.Contains("hreflang=\"x-default\" href=\"https://nova-globen.se/\"", head);
        Assert.Contains("\"vatID\":\"SE559481693501\"", head);
        Assert.Contains("559481-6935", head);
        Assert.Equal("+46850431312", System.Text.Json.JsonDocument.Parse(ThemeHead.Organization("https://nova-globen.se"))
            .RootElement.GetProperty("telephone").GetString());
        // the full postal address is on the contact and legal pages only; the company data names the place
        var address = System.Text.Json.JsonDocument.Parse(ThemeHead.Organization("https://nova-globen.se")).RootElement.GetProperty("address");
        Assert.Equal("Bromma", address.GetProperty("addressLocality").GetString());
        Assert.Equal("SE", address.GetProperty("addressCountry").GetString());
        Assert.False(address.TryGetProperty("streetAddress", out _));
        Assert.False(address.TryGetProperty("postalCode", out _));
        Assert.Contains("og:image\" content=\"https://nova-globen.se/assets/og-image.png\"", head);
        Assert.Contains("application/atom+xml", head);
    }

    [Fact]
    public void A_404_page_is_noindex_and_has_no_canonical()
    {
        var head = ThemeHead.Build(new ThemeHead.Input("https://nova-globen.se", "/404", "T", "", "en", null!, "sv", "website", "", "", false, true, false));

        Assert.Contains("name=\"robots\" content=\"noindex\"", head);
        Assert.DoesNotContain("rel=\"canonical\"", head);
        Assert.DoesNotContain("hreflang", head);
    }

    [Fact]
    public void Json_ld_cannot_close_its_script_element()
    {
        var head = ThemeHead.Build(new ThemeHead.Input("https://x.test", "/", "T", "", "en", null!, "sv", "", "", "{\"a\":\"</script><b>\"}", false, false, false));

        Assert.Contains("<\\/script>", head);
        Assert.Equal(1, head.Split("</script>").Length - 1);
    }
}
