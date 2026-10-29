using NovaGloben.Website.Sites;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>Which requests are the website's: its sites, never a module's tenant or route on the same host.</summary>
public class WebsiteScopeTests
{
    private static readonly string[] WebsiteFolders = { "", "sv" };

    [Theory]
    [InlineData("", "/", true)]
    [InlineData("", "/core", true)]
    [InlineData("", "/products/vacha/releases.json", true)]
    [InlineData("sv", "/sv/core", true)]
    [InlineData("/sv/", "/sv/products/vacha/issues", true)]
    [InlineData("didin", "/didin", false)]
    [InlineData("didin", "/didin/api/items", false)]
    [InlineData("lek", "/lek/", false)]
    [InlineData("", "/fravashi", false)]
    [InlineData("", "/fravashi/api/session", false)]
    [InlineData("", "/fravashi/updates/release-manifest.json", false)]
    [InlineData("", "/fravashiX", true)]
    [InlineData(null, "/", true)]
    public void A_request_is_the_websites_only_on_its_sites_and_off_module_routes(string? sitePath, string requestPath, bool website) =>
        Assert.Equal(website, WebsiteScope.IsWebsiteRequest(sitePath, requestPath, WebsiteFolders));
}
