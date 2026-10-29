using Delato.Server.Controllers;
using Delato.Server.Services;
using Delato.Server.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Stomme.Models;
using Stomme.Shared;

namespace Delato.Tests;

/// <summary>Delato answers on the Delato site only: the host maps a module's controllers for every site.</summary>
public sealed class SiteGuardTests
{
    private static HttpContext Http(string? aliasName, string path = "/")
    {
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        if (aliasName is not null)
        {
            http.Items[Constants.HttpContextAliasKey] = new Alias { Name = aliasName, SiteId = 7 };
        }
        return http;
    }

    private static ResourceExecutingContext Request(string? aliasName) =>
        new(new ActionContext(Http(aliasName), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());

    [Theory]
    [InlineData("nova-globen.test/delato")]
    [InlineData("uat.example.test/DELATO")]
    public void A_request_to_the_Delato_site_goes_through_and_knows_its_site(string alias)
    {
        var context = Request(alias);

        new DelatoSiteOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeNull();
        context.HttpContext.Items[DelatoSiteOnlyAttribute.ItemKey].Should().BeOfType<DelatoSite>()
            .Which.Should().Be(new DelatoSite(7, "/" + alias.Split('/')[1]));
    }

    [Theory]
    [InlineData("nova-globen.test")]          // the root site (the company site)
    [InlineData("nova-globen.test/sv")]       // another folder site
    [InlineData("nova-globen.test/lek")]      // another module's tenant
    [InlineData("nova-globen.test/didin")]    // another module's tenant
    [InlineData("nova-globen.test/delatox")]
    [InlineData(null)]                        // no site resolved at all
    public void A_request_to_any_other_site_is_not_found(string? alias)
    {
        var context = Request(alias);

        new DelatoSiteOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeOfType<NotFoundResult>();
        context.HttpContext.Items.ContainsKey(DelatoSiteOnlyAttribute.ItemKey).Should().BeFalse();
    }

    [Fact]
    public void Every_Delato_controller_carries_the_guard()
    {
        var controllers = typeof(DelatoController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        controllers.Should().NotBeEmpty();
        controllers.Should().AllSatisfy(t => t.Should().BeDerivedFrom<DelatoController>());
        typeof(DelatoController).GetCustomAttributes(typeof(DelatoSiteOnlyAttribute), inherit: true).Should().ContainSingle();
    }

    [Fact]
    public void The_api_guard_knows_every_controller_route() =>
        DelatoApiGuard.Routes.Should().BeEquivalentTo(["/api/v1/groups", "/api/v1/me", "/api/v1/currencies", "/api/v1/import"]);

    [Theory]
    [InlineData("nova-globen.test", "/api/v1/groups")]
    [InlineData("nova-globen.test/lek", "/api/v1/groups/3/report")]
    [InlineData("nova-globen.test/didin", "/api/v1/me/summary")]
    [InlineData(null, "/api/v1/currencies")]
    public void The_api_guard_refuses_the_addresses_off_the_Delato_site(string? alias, string path) =>
        DelatoApiGuard.Refuses(Http(alias, path)).Should().BeTrue();

    [Theory]
    [InlineData("nova-globen.test/delato", "/api/v1/groups")]
    [InlineData("nova-globen.test", "/api/me")]          // another module's own address
    [InlineData("nova-globen.test", "/")]
    public void The_api_guard_lets_everything_else_through(string? alias, string path) =>
        DelatoApiGuard.Refuses(Http(alias, path)).Should().BeFalse();
}
