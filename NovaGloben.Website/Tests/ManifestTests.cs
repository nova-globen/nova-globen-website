using System.Text.Json;
using Microsoft.AspNetCore.Http;
using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>
/// Vacha's update manifest: its shape, its headers, and that it names the version the download page offers. It lives at
/// /products/vacha/releases.json; installed Vacha 1.1.0 reads /vacha/releases.json and Tond 1.0.0 (its old name)
/// /tond/releases.json, which answer a 301 to it that a cross-origin fetch may follow.
/// </summary>
public class ManifestTests
{
    /// <summary>
    /// What the update prompt's Download button opens, in the user's browser: the installer of the latest GitHub Release,
    /// which the browser downloads. Always https.
    /// </summary>
    private const string Installer = "https://github.com/nova-globen/vacha/releases/latest/download/vacha-setup-x64.exe";

    [Fact]
    public void The_manifest_has_the_shape_the_app_reads()
    {
        using var json = JsonDocument.Parse(ProductManifests.Read("/products/vacha/releases.json")!);
        var root = json.RootElement;

        Assert.Matches(@"^\d+\.\d+\.\d+$", root.GetProperty("version").GetString());
        Assert.Equal(Installer, root.GetProperty("url").GetString());
        var x64 = root.GetProperty("platforms").GetProperty("win32-x64");
        Assert.Equal(root.GetProperty("version").GetString(), x64.GetProperty("version").GetString());
        Assert.Equal(Installer, x64.GetProperty("url").GetString());
    }

    [Theory]
    [InlineData("company-en")]
    [InlineData("company-sv")]
    public void The_download_page_offers_the_version_the_manifest_names(string site)
    {
        using var json = JsonDocument.Parse(ProductManifests.Read("/products/vacha/releases.json")!);
        var version = json.RootElement.GetProperty("version").GetString();
        var page = File.ReadAllText(Path.Combine(ContentTests.RepositoryRoot(), "content", "sites", site, "pages", "product-vacha-download.html"));

        Assert.Contains($"Version {version} ", page);
    }

    [Fact]
    public async Task The_manifest_answers_json_that_any_origin_may_read()
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/products/vacha/releases.json";
        context.Response.Body = new MemoryStream();
        var nextCalled = false;

        await ProductManifests.InvokeAsync(context, () => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(200, context.Response.StatusCode);
        Assert.Equal("*", context.Response.Headers["Access-Control-Allow-Origin"].ToString());
        Assert.StartsWith("application/json", context.Response.ContentType);
        context.Response.Body.Position = 0;
        Assert.Contains("\"version\"", new StreamReader(context.Response.Body).ReadToEnd());
    }

    [Theory]
    [InlineData("/vacha/releases.json")]
    [InlineData("/tond/releases.json")]
    public async Task An_old_manifest_address_moves_permanently_and_any_origin_may_follow(string path)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = path;
        var nextCalled = false;

        await ProductManifests.InvokeAsync(context, () => { nextCalled = true; return Task.CompletedTask; });

        Assert.False(nextCalled);
        Assert.Equal(301, context.Response.StatusCode);
        Assert.Equal("/products/vacha/releases.json", context.Response.Headers.Location.ToString());
        Assert.Equal("*", context.Response.Headers["Access-Control-Allow-Origin"].ToString());
        Assert.NotNull(ProductManifests.Read(context.Response.Headers.Location.ToString())); // one hop
    }

    [Fact]
    public void Any_other_address_is_not_a_manifest() => Assert.Null(ProductManifests.Read("/vacha/other.json"));
}
