using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using Fravashi.Broker.Endpoints;
using Fravashi.Broker.RateLimiting;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>What a person reads of their own usage (<c>GET /broker/usage/me</c>): their figures only, read without spending.</summary>
public sealed class PersonalUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Started = Now.AddHours(-3);

    private readonly BrokerServiceOptions _options = new()
    {
        QuotaRequests = 300,
        QuotaTokens = 1_000_000,
        QuotaWindow = TimeSpan.FromDays(1),
    };
    private readonly TestTimeProvider _clock = new(Now);
    private readonly InMemoryUsageMeter _meter;
    private readonly InMemoryTokenBudget _budget;
    private readonly Suspensions _suspensions;

    public PersonalUsageTests()
    {
        _meter = new InMemoryUsageMeter(Options.Create(_options), _clock);
        _budget = new InMemoryTokenBudget(Options.Create(_options), _clock);
        _suspensions = new Suspensions(Options.Create(_options), _clock);
    }

    private PersonalUsage Read(string user) => PersonalUsage.For(user, _meter, _budget, _suspensions, _options, Started, _clock.GetUtcNow());

    [Fact]
    public void Before_any_request_a_person_has_used_nothing_and_no_window_is_running()
    {
        Read("ada").Should().Be(new PersonalUsage(Now, Started, 86_400, 0, 1_000_000, null, 0, 300, null, false));
    }

    [Fact]
    public void A_person_reads_their_own_figures_with_the_time_each_window_turns_and_nobody_elses()
    {
        _meter.TryConsume("ada");
        _clock.Advance(TimeSpan.FromSeconds(2));
        _budget.Charge("ada", 1_200, 1_000);
        _meter.TryConsume("ada");
        _budget.Charge("ada", 300, 250);
        _meter.TryConsume("bo");
        _budget.Charge("bo", 9_999);

        var ada = Read("ada");
        (ada.RequestsUsed, ada.UnitsUsed).Should().Be((2, 1_500L));
        ada.RequestsResetAtUtc.Should().Be(Now.AddDays(1));
        ada.UnitsResetAtUtc.Should().Be(Now.AddSeconds(2).AddDays(1), "the units window starts at the first charge");
        ada.CountedSinceUtc.Should().Be(Started);

        Read("ada").Should().Be(ada, "reading spends nothing");
    }

    [Fact]
    public void When_the_window_turns_the_figures_start_again()
    {
        _meter.TryConsume("ada");
        _budget.Charge("ada", 500);

        _clock.Advance(TimeSpan.FromDays(1));

        var ada = Read("ada");
        (ada.RequestsUsed, ada.UnitsUsed, ada.RequestsResetAtUtc, ada.UnitsResetAtUtc).Should().Be((0, 0L, (DateTimeOffset?)null, (DateTimeOffset?)null));
    }

    [Fact]
    public void Without_a_unit_budget_the_limit_is_null()
    {
        _options.QuotaTokens = 0;

        Read("ada").UnitsLimit.Should().BeNull();
    }
}
