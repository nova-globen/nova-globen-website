using Microsoft.AspNetCore.Http;
using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>What a visitor's request carries into the site: a stale culture cookie, and the cache of a theme file.</summary>
public class VisitorRequestTests
{
    private static HttpRequest Request(string cookie)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = cookie;
        return context.Request;
    }

    [Fact]
    public void A_stale_culture_cookie_is_taken_off_and_the_others_stay()
    {
        var request = Request(".AspNetCore.Culture=c%3Den%7Cuic%3Den; stomme-release=10.74.1; X-XSRF-TOKEN-COOKIE=abc");
        Assert.True(request.Cookies.ContainsKey(VisitorCookies.CultureCookie)); // parsed once before

        Assert.True(VisitorCookies.WithoutCultureCookie(request));

        Assert.False(request.Cookies.ContainsKey(VisitorCookies.CultureCookie));
        Assert.Equal("10.74.1", request.Cookies["stomme-release"]);
        Assert.Equal("abc", request.Cookies["X-XSRF-TOKEN-COOKIE"]);
    }

    [Fact]
    public void A_request_without_the_cookie_is_left_as_it_is()
    {
        var request = Request("stomme-release=10.74.1");

        Assert.False(VisitorCookies.WithoutCultureCookie(request));
        Assert.Equal("stomme-release=10.74.1", request.Headers.Cookie.ToString());
    }

    [Theory]
    [InlineData("/Themes/NovaGloben.Website.Client.Themes.NovaGloben/theme.css", "?v=10513C50", true)]
    [InlineData("/Themes/NovaGloben.Website.Client.Themes.NovaGloben/logo-full.svg", "", false)]
    [InlineData("/Themes/NovaGloben.Website.Client.Themes.NovaGloben/theme.css", "?v=", false)]
    [InlineData("/css/app.css", "?v=36F6AD18", false)]
    [InlineData("/assets/products/stomme.svg", "?v=1", false)]
    public void Only_a_fingerprinted_theme_file_is_kept_a_year(string path, string query, bool immutable)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Request.QueryString = new QueryString(query.Length > 0 ? query : null);

        Assert.Equal(immutable, AssetCaching.IsFingerprintedThemeAsset(context.Request));
    }
}
