using System.Reflection;
using Delato.Server.Controllers;
using Delato.Server.Services;
using Delato.Server.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Stomme.Models;
using Stomme.Shared;

namespace Delato.Tests;

/// <summary>
/// Delato is OFF unless the host turns it on (module.delato.enabled=true), and off it shows nothing: no endpoint, no
/// hosted service, no site.
/// </summary>
public sealed class SwitchOffTests
{
    private sealed class Config(bool? delato) : Stomme.FeatureManagement.Application.IFeatureConfigurationSource
    {
        public bool? GetConfiguredState(string key) => key == "module.delato.enabled" ? delato : null;
    }

    private static DefaultHttpContext Http(bool off, string alias = "x.test/delato", string path = "/")
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(new DelatoSwitch(new Config(off ? null : true))).BuildServiceProvider(),
        };
        http.Items[Constants.HttpContextAliasKey] = new Alias { Name = alias, SiteId = 4 };
        http.Request.Path = path;
        return http;
    }

    [Theory]
    [InlineData(null, true)]    // a host that says nothing - production - has it off
    [InlineData(false, true)]
    [InlineData(true, false)]   // only an explicit true turns it on: the UAT compose
    public void The_switch_defaults_off(bool? configured, bool off) =>
        new DelatoSwitch(new Config(configured)).IsOff.Should().Be(off);

    [Fact]
    public void The_switch_key_is_module_delato_enabled() =>
        DelatoSwitch.Key.Should().Be("module.delato.enabled");

    [Theory]
    [InlineData(false, true, false, DelatoSiteReconciler.Step.Wire)]
    [InlineData(true, true, false, DelatoSiteReconciler.Step.Nothing)]
    [InlineData(false, false, true, DelatoSiteReconciler.Step.Ensure)]
    [InlineData(true, false, true, DelatoSiteReconciler.Step.Hide)]
    [InlineData(true, false, false, DelatoSiteReconciler.Step.Nothing)]
    [InlineData(false, false, false, DelatoSiteReconciler.Step.Nothing)]
    public void Off_the_reconciler_creates_and_wires_nothing_and_hides_the_site(bool off, bool ours, bool root, DelatoSiteReconciler.Step step) =>
        DelatoSiteReconciler.Decide(off, ours, root).Should().Be(step);

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Off_the_controllers_and_the_guard_answer_404_even_on_the_Delato_site(bool off, bool refused)
    {
        var context = new ResourceExecutingContext(new ActionContext(Http(off), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());
        new DelatoSiteOnlyAttribute().OnResourceExecuting(context);

        (context.Result is NotFoundResult).Should().Be(refused);
        DelatoApiGuard.Refuses(Http(off, path: "/api/v1/groups")).Should().Be(refused);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Off_its_controllers_leave_the_application_model_and_no_other_does(bool off)
    {
        var options = new MvcOptions();
        new DelatoControllersOff(new DelatoSwitch(off)).Configure(options);
        var application = new ApplicationModel();
        application.Controllers.Add(new ControllerModel(typeof(GroupsController).GetTypeInfo(), new List<object>()));
        application.Controllers.Add(new ControllerModel(typeof(MeController).GetTypeInfo(), new List<object>()));
        application.Controllers.Add(new ControllerModel(typeof(OtherController).GetTypeInfo(), new List<object>()));

        foreach (var convention in options.Conventions.OfType<IApplicationModelConvention>()) convention.Apply(application);

        application.Controllers.Select(c => c.ControllerType.AsType()).Should().BeEquivalentTo(off
            ? new[] { typeof(OtherController) }
            : new[] { typeof(GroupsController), typeof(MeController), typeof(OtherController) });
    }

    private sealed class OtherController : ControllerBase
    {
    }

    [Fact]
    public void It_has_no_hosted_service_of_its_own()
    {
        var services = new ServiceCollection();
        new DelatoServerStartup().ConfigureServices(services);

        // The one hosted service is the host's shared module-store initializer, which every module's
        // AddModuleAggregatePersistence registers (once): it creates the store's tables and runs nothing of Delato.
        services.Where(d => d.ServiceType == typeof(IHostedService))
            .Select(d => d.ImplementationType?.FullName)
            .Should().Equal("Microsoft.Extensions.DependencyInjection.ModuleAggregateStoreInitializer");
        typeof(DelatoSwitch).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(IHostedService).IsAssignableFrom(t))
            .Should().BeEmpty("nothing of Delato may run in the background where it is off");
    }

    [Fact]
    public void Its_only_endpoints_are_its_controllers_and_its_only_middleware_the_guard_and_the_mail_gate()
    {
        var services = new ServiceCollection();
        new DelatoServerStartup().ConfigureServices(services);

        // endpoints come only from MVC controllers, which DelatoControllersOff removes; nothing maps routes itself
        typeof(DelatoSwitch).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && t.GetInterfaces().Any(i => i.Name.Contains("Endpoint", StringComparison.Ordinal)))
            .Should().BeEmpty();
        typeof(DelatoSwitch).Assembly.GetTypes()
            .Where(t => !t.IsAbstract && typeof(Stomme.Infrastructure.IServerPipelineStartup).IsAssignableFrom(t))
            .Should().BeEquivalentTo([typeof(DelatoApiGuard), typeof(DelatoMailBudgetGate)]);
        services.Should().Contain(d => d.ImplementationType == typeof(DelatoControllersOff));
        services.Should().Contain(d => d.ServiceType == typeof(DelatoSwitch));
    }
}
