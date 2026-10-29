using System.Text;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using Fravashi.Broker.Integrity;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// The budgets count weighted units: each kind of token at its price relative to a fresh input token, so a free tier
/// is measured in what it costs, not in how many tokens a CLI's cached system prompt adds up to.
/// </summary>
public class TokenWeightTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);

    private static (InMemoryTokenBudget Budget, ForwardContainment Containment) Setup(TokenWeights? weights = null, long unreadable = 500)
    {
        var options = Options.Create(new BrokerServiceOptions
        {
            QuotaTokens = 1_000_000,
            QuotaWindow = TimeSpan.FromDays(1),
            OrganisationTokensPerDay = 10_000_000,
            UnreadableUsageCharge = unreadable,
            SuspendAfterRefusals = 0,
        });
        var clock = new TestTimeProvider(Now);
        var budget = new InMemoryTokenBudget(options, clock);
        return (budget, new ForwardContainment(new ModelPolicy([]), budget, new Suspensions(options, clock), weights));
    }

    [Theory]
    [InlineData(1000, 0, 0, 0, 1000)]      // fresh input: 1
    [InlineData(0, 1000, 0, 0, 1250)]      // a cache write: 1.25
    [InlineData(0, 0, 1000, 0, 100)]       // a cache read: 0.1
    [InlineData(0, 0, 0, 1000, 5000)]      // output: 5
    [InlineData(0, 0, 1, 0, 1)]            // rounded up: a run of tiny answers never costs nothing
    [InlineData(12, 36_400, 0, 101, 46_017)] // a fresh conversation: 12 + 45,500 + 505
    public void Each_kind_of_token_weighs_its_default_price(long input, long cacheWrite, long cacheRead, long output, long units)
    {
        new TokenWeights().Units(new TokenUsage(input, cacheWrite, cacheRead, output)).Should().Be(units);
    }

    [Fact]
    public void A_turn_that_reads_its_cached_prompt_costs_a_tenth_of_it()
    {
        var (budget, containment) = Setup();

        containment.After("alice", "/v1/messages", 200, new TokenUsage(20, 0, 36_400, 80));

        budget.UsedBy("alice").Should().Be(20 + 3_640 + 400);
        budget.RawUsedBy("alice").Should().Be(20 + 36_400 + 80);
    }

    [Fact]
    public void The_weights_are_configurable()
    {
        var options = new BrokerServiceOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Broker:TokenWeights:CacheRead"] = "0.2",
                ["Broker:TokenWeights:Output"] = "4",
            })
            .Build()
            .GetSection(BrokerServiceOptions.SectionName)
            .Bind(options);

        options.TokenWeights.Input.Should().Be(1);
        options.TokenWeights.CacheWrite.Should().Be(1.25);
        options.TokenWeights.Units(new TokenUsage(0, 0, 1000, 1000)).Should().Be(200 + 4000);
    }

    [Fact]
    public void The_scanner_keeps_each_figure_apart()
    {
        var scanner = new UsageScanner();
        scanner.Feed(Encoding.UTF8.GetBytes(
            """{"usage":{"input_tokens":1200,"cache_creation_input_tokens":300,"cache_read_input_tokens":4000,"output_tokens":250}}"""));

        scanner.Usage.Should().Be(new TokenUsage(1200, 300, 4000, 250));
        scanner.Tokens.Should().Be(5750);
    }

    [Fact]
    public void A_figure_never_seen_counts_as_zero_and_no_figure_at_all_is_unreadable()
    {
        var some = new UsageScanner();
        some.Feed(Encoding.UTF8.GetBytes("""{"usage":{"output_tokens":7}}"""));
        var none = new UsageScanner();
        none.Feed(Encoding.UTF8.GetBytes("""{"content":"no usage here"}"""));

        some.Usage.Should().Be(new TokenUsage(0, 0, 0, 7));
        none.Usage.Should().BeNull();
    }

    [Fact]
    public void An_unreadable_usage_is_still_charged_the_worst_case_in_units()
    {
        var (budget, containment) = Setup(unreadable: 200_000);

        containment.After("alice", "/v1/messages", 200, null).Should().BeNull();

        budget.UsedBy("alice").Should().Be(200_000);
        budget.RawUsedBy("alice").Should().Be(0);
        budget.OrganisationUsed().Should().Be((200_000L, 0L));
    }

    [Fact]
    public void The_organisation_counts_everyone_in_both_figures()
    {
        var (budget, containment) = Setup();

        containment.After("alice", "/v1/messages", 200, new TokenUsage(0, 0, 0, 100));
        containment.After("bob", "/v1/messages", 200, new TokenUsage(1000, 0, 0, 0));

        budget.OrganisationUsed().Should().Be((500L + 1000L, 100L + 1000L));
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void A_weight_that_is_not_a_sane_number_stops_the_broker(double weight)
    {
        var options = new BrokerServiceOptions { TokenWeights = new TokenWeights { CacheRead = weight } };

        options.TokenWeights.Problems().Should().ContainSingle().Which.Should().Contain("Broker:TokenWeights:CacheRead");
        new TokenWeights().Problems().Should().BeEmpty();
    }

    // ---- long context -------------------------------------------------------------------------------------

    [Theory]
    [InlineData(100_000, 0, 0, 1000, 105_000)]            // exactly the threshold: not long, 100,000 + 5,000
    [InlineData(100_001, 0, 0, 1000, 525_005)]            // one more: (100,001 + 5,000) x 5
    [InlineData(1_000, 10_000, 90_000, 0, 112_500)]       // writes and reads count toward the prompt (101,000): (1,000 + 12,500 + 9,000) x 5
    public void A_prompt_past_the_threshold_costs_the_long_context_multiple(long input, long cacheWrite, long cacheRead, long output, long units)
    {
        new TokenWeights().Units(new TokenUsage(input, cacheWrite, cacheRead, output)).Should().Be(units);
    }

    [Fact]
    public void The_long_context_threshold_and_multiplier_are_configurable()
    {
        var options = new BrokerServiceOptions();
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Broker:TokenWeights:LongContextThreshold"] = "1000",
                ["Broker:TokenWeights:LongContextMultiplier"] = "2",
            })
            .Build()
            .GetSection(BrokerServiceOptions.SectionName)
            .Bind(options);

        options.TokenWeights.Units(new TokenUsage(1000, 0, 0, 0)).Should().Be(1000);
        options.TokenWeights.Units(new TokenUsage(1001, 0, 0, 0)).Should().Be(2002);
    }

    [Theory]
    [InlineData(0, 5.0, "LongContextThreshold")]
    [InlineData(-1, 5.0, "LongContextThreshold")]
    [InlineData(100_000, 0.5, "LongContextMultiplier")]
    [InlineData(100_000, double.NaN, "LongContextMultiplier")]
    [InlineData(100_000, 1001.0, "LongContextMultiplier")]
    public void A_long_context_setting_that_makes_no_sense_stops_the_broker(long threshold, double multiplier, string key)
    {
        var weights = new TokenWeights { LongContextThreshold = threshold, LongContextMultiplier = multiplier };

        weights.Problems().Should().ContainSingle().Which.Should().Contain("Broker:TokenWeights:" + key);
    }

    // ---- the per-user budget counts units, end to end ------------------------------------------------------

    [Fact]
    public void The_per_user_budget_is_spent_in_weighted_units_not_raw_tokens()
    {
        var options = Options.Create(new BrokerServiceOptions
        {
            QuotaTokens = 10_000,
            QuotaWindow = TimeSpan.FromDays(1),
            SuspendAfterRefusals = 0,
        });
        var clock = new TestTimeProvider(Now);
        var budget = new InMemoryTokenBudget(options, clock);
        var containment = new ForwardContainment(new ModelPolicy([]), budget, new Suspensions(options, clock));

        // 50,000 raw tokens of cache reads are 5,000 units: far past 10,000 raw, still inside 10,000 units
        containment.After("alice", "/v1/messages", 200, new TokenUsage(0, 0, 50_000, 0));
        budget.Check("alice").Allowed.Should().BeTrue();
        budget.RawUsedBy("alice").Should().Be(50_000);

        // 5,000 more units: the budget is spent and the next request is refused as the user's
        containment.After("alice", "/v1/messages", 200, new TokenUsage(0, 0, 50_000, 0));
        var decision = budget.Check("alice");
        decision.Allowed.Should().BeFalse();
        decision.Limit.Should().Be("user");
        budget.UsedBy("alice").Should().Be(10_000);
    }
}
