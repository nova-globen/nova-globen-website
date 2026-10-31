using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>The Content-Security-Policy: what it allows, and what it refuses.</summary>
public class SecurityHeadersTests
{
    private const string Bootstrap = "\n// theme bootstrap\ndocument.documentElement.dataset.x = '1';\n";

    private static string Hash(string script) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));

    private static string Page(string head = "", string body = "") =>
        $"<!DOCTYPE html><html><head><title>T</title><script>{Bootstrap}</script>{head}</head><body>{body}" +
        "<script src=\"_framework/blazor.web.js\"></script></body></html>";

    [Theory]
    [InlineData(null, false)]
    [InlineData("false", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    public void A_host_is_kept_out_of_search_engines_only_when_it_says_so(string? value, bool noIndex)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [SecurityHeaders.NoIndexKey] = value }).Build();

        Assert.Equal(noIndex, SecurityHeaders.IsNoIndex(configuration));
        Assert.Equal(noIndex ? "User-agent: *\nDisallow: /\n" : "User-agent: *\nAllow: /\n", SecurityHeaders.Robots(noIndex));
    }

    [Fact]
    public void The_head_bootstrap_is_allowed_by_its_hash()
    {
        var policy = SecurityHeaders.Policy(Page());

        Assert.Contains($"'sha256-{Hash(Bootstrap)}'", policy);
        Assert.Contains("script-src 'self' 'sha256-", policy);
        Assert.DoesNotContain("'unsafe-inline' ", policy.Split("; ").Single(d => d.StartsWith("script-src", StringComparison.Ordinal)) + " ");
    }

    [Fact]
    public void A_script_in_the_body_is_not_allowed()
    {
        const string injected = "alert(1)";
        var policy = SecurityHeaders.Policy(Page(body: $"<p>text</p><script>{injected}</script>"));

        Assert.DoesNotContain(Hash(injected), policy);
        Assert.Single(SecurityHeaders.HeadScriptHashes(Page(body: $"<script>{injected}</script>")));
    }

    [Fact]
    public void Json_ld_and_external_scripts_are_not_hashed()
    {
        var hashes = SecurityHeaders.HeadScriptHashes(Page(head:
            "<script type=\"application/ld+json\">{\"@type\":\"Organization\"}</script><script src=\"/x.js\"></script>"));

        Assert.Equal(new[] { Hash(Bootstrap) }, hashes);
    }

    [Fact]
    public void Turnstile_is_allowed_only_on_a_page_that_loads_it()
    {
        var plain = SecurityHeaders.Policy(Page());
        var contact = SecurityHeaders.Policy(Page(head: $"<script src=\"{SecurityHeaders.Turnstile}/turnstile/v0/api.js\" async defer></script>"));

        Assert.DoesNotContain(SecurityHeaders.Turnstile, plain);
        Assert.Contains("frame-src 'none'", plain);
        Assert.Contains($"script-src 'self' 'sha256-{Hash(Bootstrap)}' {SecurityHeaders.Turnstile}", contact);
        Assert.Contains($"frame-src {SecurityHeaders.Turnstile}", contact);
    }

    private const string AnalyticsToken = "45ecd45888c6481f93ae6d5bb5e4bbea";

    private sealed class Environment(string name) : Microsoft.Extensions.Hosting.IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Theory]
    [InlineData(AnalyticsToken, null, "Production", true)]
    [InlineData(AnalyticsToken, "true", "Production", false)]     // the test host, kept out of search engines
    [InlineData(AnalyticsToken, null, "Development", false)]
    [InlineData(null, null, "Production", false)]
    [InlineData("", null, "Production", false)]
    [InlineData("not-a-token\"><script>", null, "Production", false)]
    public void The_analytics_beacon_is_on_only_on_the_public_host(string? token, string? noIndex, string environment, bool on)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [WebAnalytics.TokenKey] = token,
            [SecurityHeaders.NoIndexKey] = noIndex,
        }).Build();

        Assert.Equal(on ? token : null, WebAnalytics.Token(configuration, new Environment(environment)));
    }

    [Fact]
    public void The_beacon_goes_before_the_body_closes_once_and_opens_its_two_origins()
    {
        var page = WebAnalytics.Add(Page(body: "<p>x</p>"), AnalyticsToken);
        var policy = SecurityHeaders.Policy(page);

        Assert.Contains($"<script defer src=\"{WebAnalytics.ScriptOrigin}/beacon.min.js\" data-cf-beacon='{{\"token\": \"{AnalyticsToken}\"}}'></script></body>", page);
        Assert.Equal(page, WebAnalytics.Add(page, AnalyticsToken));
        Assert.Contains($"script-src 'self' 'sha256-{Hash(Bootstrap)}' {WebAnalytics.ScriptOrigin}", policy);
        Assert.Contains($"connect-src 'self' {WebAnalytics.ReportOrigin}", policy);

        var plain = SecurityHeaders.Policy(Page());
        Assert.DoesNotContain("cloudflareinsights", plain);
        Assert.Contains("connect-src 'self';", plain);
    }

    [Fact]
    public void Plugins_framing_by_others_and_foreign_form_targets_are_refused()
    {
        var policy = SecurityHeaders.Policy(Page());

        Assert.Contains("object-src 'none'", policy);
        Assert.Contains("frame-ancestors 'self'", policy);
        Assert.Contains("form-action 'self'", policy);
        Assert.Contains("base-uri 'self'", policy);
    }

    [Fact]
    public void An_interactive_page_is_recognised_by_its_blazor_marker()
    {
        Assert.True(SecurityHeaders.IsInteractive("<body><!--Blazor:{\"type\":\"server\"}--><div></div></body>"));
        Assert.False(SecurityHeaders.IsInteractive(Page()));
    }

    [Fact]
    public void The_font_preload_names_the_file_the_theme_loads()
    {
        var theme = File.ReadAllText(Path.Combine(ContentTests.RepositoryRoot(), "NovaGloben.Website", "Client", "wwwroot", "Themes",
            "NovaGloben.Website.Client.Themes.NovaGloben", "theme.css"));
        var file = NovaGloben.Website.Client.Themes.NovaGloben.ThemeHead.FontPath.Split('/').Last();

        Assert.Contains($"url(\"fonts/inter/{file}\")", theme);
    }

    [Theory]
    [InlineData("GET", "/", true)]
    [InlineData("GET", "/blog/versioning-rest-api", true)]
    [InlineData("HEAD", "/sv/contact", true)]
    [InlineData("POST", "/contact-form", false)]
    [InlineData("GET", "/sitemap.xml", false)]
    [InlineData("GET", "/Themes/NovaGloben/theme.css", false)]
    [InlineData("GET", "/_framework/blazor.web.js", false)]
    [InlineData("GET", "/_blazor", false)]
    public void Only_visitor_pages_are_buffered(string method, string path, bool buffered)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;

        Assert.Equal(buffered, SecurityHeaders.IsVisitorPage(context));
    }
}
