using Didin.Server.Controllers;
using Didin.Server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Stomme.Models;
using Stomme.Shared;

namespace Didin.Tests;

/// <summary>Didin answers on the Didin site only: the host maps a module's controllers for every site.</summary>
public sealed class SiteGuardTests
{
    private static ResourceExecutingContext Request(string? aliasName)
    {
        var http = new DefaultHttpContext();
        if (aliasName is not null)
        {
            http.Items[Constants.HttpContextAliasKey] = new Alias { Name = aliasName, SiteId = 7 };
        }
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ResourceExecutingContext(action, new List<IFilterMetadata>(), new List<IValueProviderFactory>());
    }

    [Theory]
    [InlineData("nova-globen.test/didin")]
    [InlineData("uat.example.test/DIDIN")]
    public void A_request_to_the_Didin_site_goes_through_and_knows_its_site(string alias)
    {
        var context = Request(alias);

        new DidinSiteOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeNull();
        context.HttpContext.Items[DidinSiteOnlyAttribute.ItemKey].Should().BeOfType<DidinSite>()
            .Which.Should().Be(new DidinSite(7, "/" + alias.Split('/')[1]));
    }

    [Theory]
    [InlineData("nova-globen.test")]          // the root site: /api/catalog there must not reach Didin
    [InlineData("nova-globen.test/sv")]       // another folder site
    [InlineData("nova-globen.test/lek")]      // another module's tenant
    [InlineData("nova-globen.test/didinx")]
    [InlineData(null)]                        // no site resolved at all
    public void A_request_to_any_other_site_is_not_found(string? alias)
    {
        var context = Request(alias);

        new DidinSiteOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeOfType<NotFoundResult>();
        context.HttpContext.Items.ContainsKey(DidinSiteOnlyAttribute.ItemKey).Should().BeFalse();
    }

    [Fact]
    public void Every_Didin_controller_carries_the_guard()
    {
        var controllers = typeof(DidinController).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t))
            .ToList();

        controllers.Should().NotBeEmpty();
        controllers.Should().AllSatisfy(t => t.Should().BeDerivedFrom<DidinController>());
        typeof(DidinController).GetCustomAttributes(typeof(DidinSiteOnlyAttribute), inherit: true).Should().ContainSingle();
    }
}
