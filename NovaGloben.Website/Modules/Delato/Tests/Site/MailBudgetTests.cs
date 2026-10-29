using Delato.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stomme.Models;
using Stomme.Shared;

namespace Delato.Tests;

/// <summary>The Delato site's share of the relay: a cap an hour and a day across every client, on the Delato site only.</summary>
public sealed class MailBudgetTests
{
    private static DelatoMailBudget Budget(FixedClock clock, Dictionary<string, string?>? values = null) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(values ?? new()).Build(), clock);

    [Fact]
    public void The_hour_cap_refuses_until_the_next_hour()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 29, 10, 50, 0, TimeSpan.Zero));
        var budget = Budget(clock, new() { ["Delato:Mail:PerHour"] = "3" });

        Enumerable.Range(0, 3).Select(i => budget.TryTake(out _)).ToList().Should().AllBeEquivalentTo(true);
        budget.TryTake(out var wait).Should().BeFalse();
        wait.Should().Be(TimeSpan.FromMinutes(10));

        clock.Now = clock.Now.AddMinutes(10);
        budget.TryTake(out _).Should().BeTrue();
    }

    [Fact]
    public void The_day_cap_holds_across_hours()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 29, 22, 0, 0, TimeSpan.Zero));
        var budget = Budget(clock, new() { ["Delato:Mail:PerHour"] = "5", ["Delato:Mail:PerDay"] = "6" });

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
        var budget = Budget(new FixedClock(DateTimeOffset.UnixEpoch), new() { ["Delato:Mail:PerHour"] = "0", ["Delato:Mail:PerDay"] = "x" });

        (budget.PerHour, budget.PerDay).Should().Be((10, 50));
        Budget(new FixedClock(DateTimeOffset.UnixEpoch)).PerDay.Should().Be(50);
    }

    [Theory]
    [InlineData("POST", "/api/User", true)]
    [InlineData("GET", "/api/User/forgotpassword/someone", true)]
    [InlineData("GET", "/api/User/forgotusername/a@b.test", true)]
    [InlineData("GET", "/api/User/loginlink/a@b.test", true)]
    [InlineData("POST", "/api/User/signin", false)]
    [InlineData("GET", "/api/User/passwordrequirements/4", false)]
    [InlineData("POST", "/api/v1/groups", false)]
    [InlineData("GET", "/api/v1/me/summary", false)]
    public void Only_requests_that_send_mail_spend_the_budget(string method, string path, bool sends)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = method;
        request.Path = path;

        DelatoMailBudget.Sends(request).Should().Be(sends);
    }

    private static DefaultHttpContext Request(string? alias, string method, string path, bool delatoOff = false)
    {
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(new DelatoSwitch(delatoOff)).BuildServiceProvider(),
        };
        if (alias is not null)
        {
            http.Items[Constants.HttpContextAliasKey] = new Alias { Name = alias, SiteId = 4 };
        }
        http.Request.Method = method;
        http.Request.Path = path;
        http.Response.Body = new MemoryStream();
        return http;
    }

    [Theory]
    [InlineData("x.test/delato", false, true)]
    [InlineData("x.test", false, false)]          // the company site's own registration is not Delato's
    [InlineData("x.test/lek", false, false)]      // Lek has its own budget
    [InlineData("x.test/didin", false, false)]
    [InlineData(null, false, false)]
    [InlineData("x.test/delato", true, false)]    // Delato off: nothing of it runs
    public void Only_the_Delato_site_spends_its_budget(string? alias, bool off, bool spends) =>
        DelatoMailBudgetGate.Spends(Request(alias, "POST", "/api/User", off)).Should().Be(spends);

    [Fact]
    public async Task Past_the_cap_the_gate_answers_429_with_retry_after_and_other_sites_pass()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 10, 29, 10, 59, 30, TimeSpan.Zero));
        var services = new ServiceCollection()
            .AddSingleton(Budget(clock, new() { ["Delato:Mail:PerHour"] = "2" }))
            .BuildServiceProvider();
        var app = new ApplicationBuilder(services);
        new DelatoMailBudgetGate().Configure(app, null!);
        app.Run(context => { context.Response.StatusCode = 204; return Task.CompletedTask; });
        var pipeline = app.Build();

        async Task<HttpContext> Send(string? alias)
        {
            var context = Request(alias, "POST", "/api/User");
            await pipeline(context);
            return context;
        }

        (await Send("x.test/delato")).Response.StatusCode.Should().Be(204);
        (await Send("x.test/delato")).Response.StatusCode.Should().Be(204);
        var refused = await Send("x.test/delato");
        refused.Response.StatusCode.Should().Be(429);
        refused.Response.Headers.RetryAfter.ToString().Should().Be("30");
        (await Send("x.test/lek")).Response.StatusCode.Should().Be(204);
        (await Send("x.test")).Response.StatusCode.Should().Be(204);
    }
}
