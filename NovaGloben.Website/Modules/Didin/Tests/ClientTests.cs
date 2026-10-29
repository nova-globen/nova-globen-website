using Didin.Server.Startup;
using Microsoft.AspNetCore.Http;
using Stomme.Models;
using Stomme.Shared;

namespace Didin.Tests;

/// <summary>The client's files, how they are cached, and the site the module sets up for them.</summary>
public sealed class ClientTests
{
    private static string ClientRoot([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.Combine(Path.GetDirectoryName(here)!, "..", "Client", "wwwroot");

    [Theory]
    [InlineData(DidinAssets.Theme)]
    [InlineData(DidinAssets.Module)]
    [InlineData(DidinAssets.QrLink)]
    public void Every_file_is_addressed_under_the_library_with_its_content_hash(string file)
    {
        var url = DidinAssets.Url(file);

        System.IO.File.Exists(Path.Combine(ClientRoot(), file)).Should().BeTrue();
        url.Should().StartWith("/_content/Didin.Client.Stomme/" + file + "?v=");
        url[(url.IndexOf("?v=") + 3)..].Should().MatchRegex("^[0-9a-f]{12}$");
        url.Should().NotEndWith("?v=000000000000");
    }

    [Theory]
    [InlineData("/_content/Didin.Client.Stomme/Theme.css", "?v=0123456789ab", DidinAssetCaching.Immutable)]
    [InlineData("/_content/Didin.Client.Stomme/Module.css", "", DidinAssetCaching.Revalidate)]
    [InlineData("/_content/Other.Library/Theme.css", "?v=1", null)]
    [InlineData("/Themes/NovaGloben.Website.Client.Themes.NovaGloben/theme.css", "?v=1", null)]
    [InlineData("/didin/api/art/1", "?v=1", null)]
    public void Only_Didin_files_get_its_cache_header(string path, string query, string? header)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;
        request.QueryString = new QueryString(query);

        DidinAssetCaching.For(request).Should().Be(header);
    }

    [Fact]
    public void The_site_base_is_the_alias_folder()
    {
        DidinAssets.SiteBase(new Alias { Name = "nova-globen.test/didin" }).Should().Be("/didin");
        DidinAssets.SiteBase(new Alias { Name = "nova-globen.test" }).Should().Be("");
        DidinAssets.SiteBase(null!).Should().Be("");
    }

    [Fact]
    public void A_new_Didin_site_is_closed_interactive_and_Didin_themed()
    {
        var site = DidinSiteReconciler.NewSite(new Site { TenantId = 3, AdminContainerType = "admin" });

        site.Name.Should().Be(DidinSiteReconciler.SiteName);
        site.TenantId.Should().Be(3);
        site.AllowRegistration.Should().BeFalse();
        site.VisitorTracking.Should().BeFalse();
        site.RenderMode.Should().Be(RenderModes.Interactive);
        site.Runtime.Should().Be(Runtimes.Server);
        site.DefaultThemeType.Should().Be("Didin.Theme.Fluent.Default, Didin.Client.Stomme");
        site.DefaultContainerType.Should().Be("Didin.Theme.Fluent.Container, Didin.Client.Stomme");
        DidinSiteReconciler.MediaModule.Should().StartWith("Didin.Module.Media");
        DidinSiteReconciler.LoginModule.Should().StartWith("Didin.Module.Login");
    }

    [Fact]
    public void Permissions_compare_as_sets()
    {
        var a = new List<Permission> { new(PermissionNames.View, RoleNames.Registered, true), new(PermissionNames.View, RoleNames.Admin, true) };
        var b = new List<Permission> { new(PermissionNames.View, RoleNames.Admin, true), new(PermissionNames.View, RoleNames.Registered, true) };

        DidinSiteReconciler.SamePermissions(a, b).Should().BeTrue();
        DidinSiteReconciler.SamePermissions(a, b.Take(1)).Should().BeFalse();
        DidinSiteReconciler.SamePermissions(null!, b).Should().BeFalse();
    }
}

/// <summary>The root site's host accounts become the Didin site's first administrators, once.</summary>
public sealed class FirstAdministratorTests
{
    private static readonly Dictionary<string, int> Roles = new()
    {
        [RoleNames.Admin] = 30, [RoleNames.Registered] = 31, ["Other"] = 32,
    };

    [Fact]
    public void Every_host_account_gets_the_administrator_and_registered_roles()
    {
        var plan = DidinSiteReconciler.PlanAdministrators([1, 2, 1], Roles, new HashSet<int>());

        plan.Should().BeEquivalentTo([(1, 30), (1, 31), (2, 30), (2, 31)]);
    }

    [Fact]
    public void A_host_granted_before_is_left_as_an_administrator_left_it()
    {
        DidinSiteReconciler.PlanAdministrators([1, 2], Roles, new HashSet<int> { 1 }).Should().BeEquivalentTo([(2, 30), (2, 31)]);
        DidinSiteReconciler.PlanAdministrators([1], Roles, new HashSet<int> { 1 }).Should().BeEmpty();
    }

    [Fact]
    public void Without_the_site_roles_nothing_is_granted()
    {
        DidinSiteReconciler.PlanAdministrators([1], new Dictionary<string, int> { [RoleNames.Admin] = 30 }, new HashSet<int>()).Should().BeEmpty();
    }

    [Fact]
    public void The_grant_is_recorded_per_account()
    {
        DidinSiteReconciler.GrantedSetting(7).Should().Be("Didin:AdministratorGranted:7");
    }
}
