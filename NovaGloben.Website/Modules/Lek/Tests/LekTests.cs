using Microsoft.AspNetCore.Mvc.ApplicationModels;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Lek.Server.Services;
using Lek.Server.Startup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Stomme.Models;
using Stomme.Shared;

namespace Lek.Tests;

/// <summary>Lek answers on the Lek site only: the host maps a module's controllers and hubs for every site.</summary>
public sealed class SiteGuardTests
{
    private static DefaultHttpContext Http(string? alias)
    {
        var http = new DefaultHttpContext();
        if (alias is not null) http.Items[Constants.HttpContextAliasKey] = new Alias { Name = alias, SiteId = 9 };
        return http;
    }

    private static ResourceExecutingContext Request(string? alias) =>
        new(new ActionContext(Http(alias), new RouteData(), new ActionDescriptor()), new List<IFilterMetadata>(), new List<IValueProviderFactory>());

    [Theory]
    [InlineData("uat.example.test/lek")]
    [InlineData("localhost:5000/LEK")]
    public void The_Lek_site_reaches_its_controllers_and_hubs(string alias)
    {
        var context = Request(alias);
        new LekSiteOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeNull();
        var hub = Http(alias); hub.Request.Path = "/hubs/fuse";
        LekHubGuard.Refuses(hub).Should().BeFalse();
    }

    [Theory]
    [InlineData("uat.example.test")]
    [InlineData("uat.example.test/didin")]
    [InlineData("uat.example.test/lekx")]
    [InlineData(null)]
    public void Any_other_site_is_turned_away(string? alias)
    {
        var context = Request(alias);
        new LekSiteOnlyAttribute().OnResourceExecuting(context);

        context.Result.Should().BeOfType<NotFoundResult>();
        foreach (var route in LekHubGuard.Routes)
        {
            var hub = Http(alias); hub.Request.Path = route + "/negotiate";
            LekHubGuard.Refuses(hub).Should().BeTrue();
        }
        var other = Http(alias); other.Request.Path = "/hubs/someone-else";
        LekHubGuard.Refuses(other).Should().BeFalse();
    }

    [Fact]
    public void Every_controller_and_hub_carries_the_guard()
    {
        var types = typeof(LekHub).Assembly.GetTypes().Where(t => !t.IsAbstract).ToList();
        var controllers = types.Where(t => typeof(ControllerBase).IsAssignableFrom(t)).ToList();
        var hubs = types.Where(t => typeof(Hub).IsAssignableFrom(t)).ToList();

        controllers.Should().HaveCount(2).And.AllSatisfy(t => t.Should().BeDerivedFrom<LekController>());
        hubs.Should().HaveCount(5).And.AllSatisfy(t => t.Should().BeDerivedFrom<LekHub>());
        LekHubGuard.Routes.Should().HaveCount(hubs.Count + controllers.Count).And.OnlyHaveUniqueItems();
    }
}

/// <summary>The game pages' addresses, and the headers their files get.</summary>
public sealed class AssetTests
{
    public static TheoryData<string> Frames() => new()
    {
        "games/fuse/index.html", "games/spark/index.html", "games/noir/index.html", "games/relicrush/index.html",
        "games/elementals/index.html", "games/elementals/classic.html", "games/insidejob/index.html", "games/silent-hour.html",
    };

    [Theory]
    [MemberData(nameof(Frames))]
    public void A_frame_names_its_page_by_hash_and_its_site(string page)
    {
        var url = LekAssets.Frame(page, new Alias { Name = "uat.example.test/lek" }, ["room=ABCD"]);

        url.Should().StartWith("/_content/Lek.Client.Stomme/" + page + "?v=");
        LekAssets.Hash(page).Should().MatchRegex("^[0-9a-f]{12}$");
        url.Should().EndWith("&base=%2Flek&room=ABCD");
    }

    [Fact]
    public void At_the_host_root_a_frame_names_no_site()
    {
        LekAssets.Frame("games/fuse/index.html", new Alias { Name = "uat.example.test" }).Should().NotContain("base=");
        LekAssets.Hash("games/none.html").Should().Be("0");
    }

    [Theory]
    [InlineData("/_content/Lek.Client.Stomme/games/noir/models/pistol.glb", "", true, true, LekStaticFiles.Revalidate)]
    [InlineData("/_content/Lek.Client.Stomme/games/fuse/index.html", "?v=0123456789ab", true, false, LekStaticFiles.Immutable)]
    [InlineData("/_content/Lek.Client.Stomme/games/fuse/fuse.js", "?v=5", true, false, LekStaticFiles.Immutable)]
    [InlineData("/_content/Didin.Client.Stomme/Theme.css", "?v=1", false, false, LekStaticFiles.Immutable)]
    [InlineData("/games/noir/models/pistol.glb", "", false, false, LekStaticFiles.Revalidate)]
    public void Only_Lek_files_are_Lek_files(string path, string query, bool ours, bool model, string cache)
    {
        var request = new DefaultHttpContext().Request;
        request.Path = path;
        request.QueryString = new QueryString(query);

        LekStaticFiles.IsOurs(request).Should().Be(ours);
        LekStaticFiles.IsModel(request).Should().Be(model);
        LekStaticFiles.CacheControl(request).Should().Be(cache);
    }
}

/// <summary>The site Lek sets up: open sign-up, confirmed e-mail.</summary>
public sealed class SiteTests
{
    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public void A_new_Lek_site_is_open_to_sign_up_and_static()
    {
        var site = LekSiteReconciler.NewSite(new Site { TenantId = 2, AdminContainerType = "admin" });

        site.Name.Should().Be("Lek");
        site.AllowRegistration.Should().BeTrue();
        site.RenderMode.Should().Be(RenderModes.Static);
        site.VisitorTracking.Should().BeFalse();
        site.TenantId.Should().Be(2);
    }

    [Fact]
    public void An_account_confirms_its_email_before_it_signs_in()
    {
        LekSiteReconciler.SiteSettings(Settings(new())).Should().ContainSingle()
            .Which.Should().Be(("LoginOptions:RequireConfirmedEmail", "true"));
    }

    [Fact]
    public void The_mail_settings_come_from_configuration_with_plain_smtp_defaults()
    {
        var mail = LekSiteReconciler.MailSettings(Settings(new() { ["Lek:Mail:Host"] = " relay ", ["Lek:Mail:Sender"] = "lek@mail.example" }));

        mail.Should().BeEquivalentTo(new List<(string, string)>
        {
            ("SMTPEnabled", "True"), ("SMTPHost", "relay"), ("SMTPPort", "25"), ("SMTPSSL", "None"),
            ("SMTPAuthentication", "Basic"), ("SMTPSender", "lek@mail.example"),
        });
        LekSiteReconciler.MailSettings(Settings(new() { ["Lek:Mail:Host"] = "relay" })).Should().BeEmpty();
    }

    [Fact]
    public void Every_game_has_its_own_page_and_module()
    {
        LekSiteReconciler.Games.Select(g => g.Path).Should().OnlyHaveUniqueItems();
        LekSiteReconciler.Games.Select(g => g.Module).Should().OnlyHaveUniqueItems().And.AllSatisfy(m => m.Should().StartWith("Lek.Module."));
        LekSiteReconciler.HeadContent.Should().MatchRegex(@"/_content/Lek\.Client\.Stomme/games/site\.css\?v=[0-9a-f]{12}");
    }
}

/// <summary>The Lek site's share of the relay: a cap an hour and a day across every client.</summary>
public sealed class MailBudgetTests
{
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static LekMailBudget Budget(Clock clock, Dictionary<string, string?>? values = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build(), clock);

    [Fact]
    public void The_hour_cap_refuses_until_the_next_hour()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 29, 10, 50, 0, TimeSpan.Zero));
        var budget = Budget(clock, new() { ["Lek:Mail:PerHour"] = "3" });

        Enumerable.Range(0, 3).Select(i => budget.TryTake(out _)).ToList().Should().AllBeEquivalentTo(true);
        budget.TryTake(out var wait).Should().BeFalse();
        wait.Should().Be(TimeSpan.FromMinutes(10));

        clock.Now = clock.Now.AddMinutes(10);
        budget.TryTake(out _).Should().BeTrue();
    }

    [Fact]
    public void The_day_cap_holds_across_hours()
    {
        var clock = new Clock(new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero));
        var budget = Budget(clock, new() { ["Lek:Mail:PerHour"] = "5", ["Lek:Mail:PerDay"] = "6" });

        Enumerable.Range(0, 5).Select(i => budget.TryTake(out _)).ToList().Should().AllBeEquivalentTo(true);
        clock.Now = clock.Now.AddHours(1);
        budget.TryTake(out _).Should().BeTrue();
        budget.TryTake(out var wait).Should().BeFalse();
        wait.Should().Be(TimeSpan.FromHours(1));
        clock.Now = clock.Now.AddHours(1);
        budget.TryTake(out _).Should().BeTrue();
    }

    [Fact]
    public void The_defaults_are_ten_an_hour_and_fifty_a_day()
    {
        var budget = Budget(new Clock(DateTimeOffset.UnixEpoch), new() { ["Lek:Mail:PerHour"] = "0", ["Lek:Mail:PerDay"] = "x" });

        (budget.PerHour, budget.PerDay).Should().Be((10, 50));
    }

    [Theory]
    [InlineData("POST", "/api/User", true)]
    [InlineData("GET", "/api/User/forgotpassword/someone", true)]
    [InlineData("GET", "/api/User/forgotusername/a@b.test", true)]
    [InlineData("GET", "/api/User/loginlink/a@b.test", true)]
    [InlineData("POST", "/api/User/signin", false)]
    [InlineData("GET", "/api/User/passwordrequirements/4", false)]
    [InlineData("GET", "/api/insidejob/me", false)]
    public void Only_requests_that_send_mail_spend_the_budget(string method, string path, bool sends)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = method;
        request.Path = path;

        LekMailBudget.Sends(request).Should().Be(sends);
    }
}

/// <summary>A host that switched Lek off (module.lek.enabled=false) shows nothing of it: no site, no API, no hub.</summary>
public sealed class SwitchOffTests
{
    private sealed class Config(bool? lek) : Stomme.FeatureManagement.Application.IFeatureConfigurationSource
    {
        public bool? GetConfiguredState(string key) => key == "module.lek.enabled" ? lek : null;
    }

    private static DefaultHttpContext Http(bool off, string path = "/")
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new Microsoft.Extensions.DependencyInjection.ServiceCollection()
                .AddSingleton(new LekSwitch(new Config(off ? false : null)))
                .BuildServiceProvider(),
        };
        http.Items[Constants.HttpContextAliasKey] = new Alias { Name = "x.test/lek", SiteId = 4 };
        http.Request.Path = path;
        return http;
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)]
    public void The_switch_is_read_as_the_host_reads_it(bool? configured, bool off)
    {
        new LekSwitch(new Config(configured)).IsOff.Should().Be(off);
    }

    [Theory]
    [InlineData(false, true, false, LekSiteReconciler.Step.Wire)]
    [InlineData(true, true, false, LekSiteReconciler.Step.Nothing)]
    [InlineData(false, false, true, LekSiteReconciler.Step.Ensure)]
    [InlineData(true, false, true, LekSiteReconciler.Step.Hide)]
    [InlineData(true, false, false, LekSiteReconciler.Step.Nothing)]
    public void Switched_off_the_reconciler_creates_and_wires_nothing_and_hides_the_site(bool off, bool ours, bool root, LekSiteReconciler.Step step)
    {
        LekSiteReconciler.Decide(off, ours, root).Should().Be(step);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void Switched_off_the_controllers_and_hubs_answer_404_even_on_the_Lek_site(bool off, bool refused)
    {
        var context = new ResourceExecutingContext(new ActionContext(Http(off), new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>(), new List<IValueProviderFactory>());
        new LekSiteOnlyAttribute().OnResourceExecuting(context);

        (context.Result is NotFoundResult).Should().Be(refused);
        LekHubGuard.Refuses(Http(off, "/hubs/fuse/negotiate")).Should().Be(refused);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Switched_off_its_controllers_leave_the_application_model_and_no_other_does(bool off)
    {
        var options = new MvcOptions();
        new LekControllersOff(new LekSwitch(off)).Configure(options);
        var application = new ApplicationModel();
        application.Controllers.Add(new ControllerModel(typeof(Lek.Server.Controllers.InsideJobController).GetTypeInfo(), new List<object>()));
        application.Controllers.Add(new ControllerModel(typeof(OtherController).GetTypeInfo(), new List<object>()));

        foreach (var convention in options.Conventions.OfType<IApplicationModelConvention>()) convention.Apply(application);

        application.Controllers.Select(c => c.ControllerType.AsType()).Should()
            .BeEquivalentTo(off ? new[] { typeof(OtherController) } : new[] { typeof(Lek.Server.Controllers.InsideJobController), typeof(OtherController) });
    }

    private sealed class OtherController : ControllerBase
    {
    }

    [Theory]
    [InlineData("x.test", "/api/insidejob/targets", true)]
    [InlineData("x.test", "/api/silenthour/progress", true)]
    [InlineData("x.test/lek", "/api/insidejob/targets", false)]
    [InlineData("x.test", "/api/catalog", false)]
    public void Another_sites_request_for_a_Lek_API_address_is_refused_before_authorization(string alias, string path, bool refused)
    {
        LekHubGuard.Routes.Should().Contain(new[] { "/api/insidejob", "/api/silenthour", "/hubs/fuse" });
        var http = new DefaultHttpContext();
        http.Request.Path = path;
        http.Items[Constants.HttpContextAliasKey] = new Alias { Name = alias, SiteId = 9 };

        LekHubGuard.Refuses(http).Should().Be(refused);
    }
}
