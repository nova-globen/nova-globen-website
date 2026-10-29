using Fravashi.Broker.Configuration;
using Fravashi.Broker.RateLimiting;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class UsageMeterTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    private static (InMemoryUsageMeter Meter, TestTimeProvider Clock) Build(
        int rate = 3, int quota = 5, TimeSpan? rateWindow = null, TimeSpan? quotaWindow = null)
    {
        var options = new BrokerServiceOptions
        {
            RateLimitRequests = rate,
            RateLimitWindow = rateWindow ?? TimeSpan.FromMinutes(1),
            QuotaRequests = quota,
            QuotaWindow = quotaWindow ?? TimeSpan.FromDays(1),
        };
        var clock = new TestTimeProvider(Now);
        return (new InMemoryUsageMeter(Options.Create(options), clock), clock);
    }

    [Fact]
    public void Under_the_rate_limit_is_allowed()
    {
        var (meter, _) = Build(rate: 3);

        for (var i = 0; i < 3; i++)
        {
            meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.Allowed);
        }
    }

    [Fact]
    public void Over_the_rate_limit_is_denied()
    {
        var (meter, _) = Build(rate: 3);

        for (var i = 0; i < 3; i++)
        {
            meter.TryConsume("alice");
        }

        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.RateLimited);
    }

    [Fact]
    public void Rate_limit_resets_after_the_window()
    {
        var (meter, clock) = Build(rate: 2, rateWindow: TimeSpan.FromMinutes(1));
        meter.TryConsume("alice");
        meter.TryConsume("alice");
        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.RateLimited);

        clock.Advance(TimeSpan.FromMinutes(1));

        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.Allowed);
    }

    [Fact]
    public void Over_the_quota_is_denied_even_within_the_rate_limit()
    {
        // Rate high enough not to interfere; quota is the binding limit.
        var (meter, _) = Build(rate: 100, quota: 4);

        for (var i = 0; i < 4; i++)
        {
            meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.Allowed);
        }

        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.QuotaExceeded);
    }

    [Fact]
    public void A_rate_limited_request_does_not_consume_quota()
    {
        var (meter, clock) = Build(rate: 2, quota: 10, rateWindow: TimeSpan.FromMinutes(1));

        meter.TryConsume("alice"); // allowed, quota=1
        meter.TryConsume("alice"); // allowed, quota=2
        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.RateLimited); // does not spend quota

        clock.Advance(TimeSpan.FromMinutes(1));

        // Only 2 of the 10 quota should have been spent; plenty remain.
        for (var i = 0; i < 2; i++)
        {
            meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.Allowed);
        }
    }

    [Fact]
    public void Limits_are_per_user()
    {
        var (meter, _) = Build(rate: 1);

        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.Allowed);
        meter.TryConsume("alice").Outcome.Should().Be(UsageOutcome.RateLimited);
        meter.TryConsume("bob").Outcome.Should().Be(UsageOutcome.Allowed);
    }
}
