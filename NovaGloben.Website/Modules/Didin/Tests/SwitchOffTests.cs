using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System.Reflection;
using Didin.Server.Controllers;
using Didin.Server.Services;
using Didin.Server.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Stomme.FeatureManagement.Application;
using Stomme.Models;
using Stomme.Shared;

namespace Didin.Tests;

/// <summary>A host that switched Didin off (module.didin.enabled=false) shows nothing of it: no site, no API.</summary>
public sealed class SwitchOffTests
{
    private sealed class Config(bool? didin) : IFeatureConfigurationSource
    {
        public bool? GetConfiguredState(string key) => key == "module.didin.enabled" ? didin : null;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)]
    public void The_switch_is_read_as_the_host_reads_it(bool? configured, bool off)
    {
        new DidinSwitch(new Config(configured)).IsOff.Should().Be(off);
    }

    [Theory]
    [InlineData(false, true, false, DidinSiteReconciler.Step.Wire)]
    [InlineData(true, true, false, DidinSiteReconciler.Step.Nothing)]
    [InlineData(false, false, true, DidinSiteReconciler.Step.Ensure)]
    [InlineData(true, false, true, DidinSiteReconciler.Step.Hide)]
    [InlineData(false, false, false, DidinSiteReconciler.Step.Nothing)]
    [InlineData(true, false, false, DidinSiteReconciler.Step.Nothing)]
    public void Switched_off_the_reconciler_creates_and_wires_nothing_and_hides_the_site(bool off, bool ours, bool root, DidinSiteReconciler.Step step)
    {
        DidinSiteReconciler.Decide(off, ours, root).Should().Be(step);
    }

    [Fact]
    public void Hiding_takes_only_the_Didin_sites_aliases()
    {
        var aliases = new[]
        {
            new Alias { AliasId = 1, SiteId = 1, Name = "x.test" },
            new Alias { AliasId = 2, SiteId = 3, Name = "x.test/didin" },
            new Alias { AliasId = 3, SiteId = 3, Name = "y.test/didin" },
            new Alias { AliasId = 4, SiteId = 2, Name = "x.test/sv" },
        };

        DidinSiteReconciler.AliasesOf(3, aliases).Select(a => a.AliasId).Should().Equal(2, 3);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Switched_off_every_Didin_controller_answers_404_even_on_its_own_site(bool off, bool notFound)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(new DidinSwitch(new Config(off ? false : null))).BuildServiceProvider(),
        };
        http.Items[Constants.HttpContextAliasKey] = new Alias { Name = "x.test/didin", SiteId = 3 };
        var context = new ResourceExecutingContext(new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());

        new DidinSiteOnlyAttribute().OnResourceExecuting(context);

        (context.Result is NotFoundResult).Should().Be(notFound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Switched_off_its_controllers_leave_the_application_model_and_no_other_does(bool off)
    {
        var options = new MvcOptions();
        new DidinControllersOff(new DidinSwitch(off)).Configure(options);
        var application = new ApplicationModel();
        application.Controllers.Add(new ControllerModel(typeof(ArtController).GetTypeInfo(), new List<object>()));
        application.Controllers.Add(new ControllerModel(typeof(OtherController).GetTypeInfo(), new List<object>()));

        foreach (var convention in options.Conventions.OfType<IApplicationModelConvention>()) convention.Apply(application);

        application.Controllers.Select(c => c.ControllerType.AsType()).Should()
            .BeEquivalentTo(off ? new[] { typeof(OtherController) } : new[] { typeof(ArtController), typeof(OtherController) });
    }

    private sealed class OtherController : ControllerBase
    {
    }

    [Theory]
    [InlineData("x.test", "/api/catalog", false, true)]
    [InlineData("x.test", "/api/admin/library/scan", false, true)]
    [InlineData("x.test/didin", "/api/catalog", false, false)]
    [InlineData("x.test/didin", "/api/catalog", true, true)]
    [InlineData("x.test", "/api/somethingelse", false, false)]
    [InlineData("x.test", "/pages/catalog", false, false)]
    public void Another_sites_request_for_a_Didin_API_address_is_refused_before_authorization(string alias, string path, bool off, bool refused)
    {
        DidinApiGuard.Routes.Should().Contain(new[] { "/api/catalog", "/api/admin/library", "/api/stream" });
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(new DidinSwitch(new Config(off ? false : null))).BuildServiceProvider(),
        };
        http.Request.Path = path;
        http.Items[Constants.HttpContextAliasKey] = new Alias { Name = alias, SiteId = 3 };

        DidinApiGuard.Refuses(http).Should().Be(refused);
    }
}
