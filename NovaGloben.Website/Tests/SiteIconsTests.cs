using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.AspNetCore.Http;
using NovaGloben.Website.Client.Themes.NovaGloben;
using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>The company's mark as the site icon: the root addresses, the files behind them, and the head's links.</summary>
public class SiteIconsTests
{
    private static string ClientWwwroot([CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Client", "wwwroot");

    [Theory]
    [InlineData("/favicon.ico", "/favicon.ico")]
    [InlineData("/FAVICON.ICO", "/favicon.ico")]
    [InlineData("/favicon.svg", "/favicon.svg")]
    [InlineData("/apple-touch-icon.png", "/apple-touch-icon.png")]
    [InlineData("/apple-touch-icon-precomposed.png", "/apple-touch-icon.png")]
    public void A_root_icon_address_is_answered_from_the_themes_folder(string address, string file)
    {
        var target = SiteIcons.FileFor(new PathString(address));

        Assert.Equal(SiteIcons.Folder + file, target);
        Assert.True(File.Exists(ClientWwwroot() + target!.Replace('/', Path.DirectorySeparatorChar)), target);
    }

    [Theory]
    [InlineData("/sv/favicon.ico")]
    [InlineData("/favicon.png")]
    [InlineData("/")]
    public void Any_other_address_is_left_alone(string address)
    {
        Assert.Null(SiteIcons.FileFor(new PathString(address)));
    }

    [Fact]
    public void The_ico_holds_16_32_and_48_pixel_images()
    {
        var ico = File.ReadAllBytes(Path.Combine(ClientWwwroot(), "Themes", "NovaGloben.Website.Client.Themes.NovaGloben", "icons", "favicon.ico"));

        Assert.Equal(new byte[] { 0, 0, 1, 0, 3, 0 }, ico[..6]);
        Assert.Equal(new[] { 16, 32, 48 }, new[] { ico[6], ico[22], ico[38] }.Select(b => (int)b));
    }

    [Fact]
    public void Every_page_head_names_the_icons_and_the_theme_colours()
    {
        var head = ThemeHead.Build(new ThemeHead.Input("https://x.test", "/", "T", "", "en", null!, "sv", "", "", "", false, false, false));

        Assert.Contains("<link rel=\"icon\" href=\"/favicon.svg\" type=\"image/svg+xml\" />", head);
        Assert.Contains("<link rel=\"apple-touch-icon\" href=\"/apple-touch-icon.png\" />", head);
        Assert.Contains("<meta name=\"theme-color\" content=\"#FFFFFF\" media=\"(prefers-color-scheme: light)\" />", head);
        Assert.Contains("<meta name=\"theme-color\" content=\"#0B1218\" media=\"(prefers-color-scheme: dark)\" />", head);
    }
}
