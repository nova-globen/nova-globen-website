using System.Text;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// Containment by METERING, with no inspection of what a conversation says. The request quota
/// counted requests, and one request can carry a very large context on the dearest model, so a user inside
/// their thousand requests could spend without bound; nothing said which models the grant covers; and nothing
/// bounded the organisation as a whole.
/// </summary>
public sealed class ContainmentTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    // ---- which models the grant covers ------------------------------------------------------------------

    [Fact]
    public void With_no_models_named_every_model_is_covered_and_the_policy_says_it_is_unrestricted()
    {
        var policy = new ModelPolicy([]);

        policy.IsRestricted.Should().BeFalse();
        policy.Check("claude-anything").Covered.Should().BeTrue();
        policy.Check(null).Covered.Should().BeTrue("with nothing named there is nothing to hold a request against");
    }

    [Theory]
    [InlineData("claude-haiku-4-5-20251001", true)]
    [InlineData("CLAUDE-HAIKU-4-5-20251001", true)]
    [InlineData("claude-sonnet-5", true)]
    [InlineData("claude-sonnet-5-20260101", true)]
    [InlineData("claude-opus-5", false)]
    [InlineData("claude-sonnet-50", true)]
    [InlineData("claude-haiku-4-5-2025100", false)]
    public void A_named_model_is_covered_exactly_or_by_a_prefix_that_ends_in_a_star(string model, bool covered)
    {
        var policy = new ModelPolicy(["claude-haiku-4-5-20251001", "claude-sonnet-5*"]);

        policy.Check(model).Covered.Should().Be(covered);
    }

    [Fact]
    public void A_request_that_names_no_model_is_not_covered_once_models_are_named()
    {
        // Fails closed: a body the broker cannot read a model out of is not a way around the list.
        var decision = new ModelPolicy(["claude-haiku-*"]).Check(null);

        decision.Covered.Should().BeFalse();
        decision.Reason.Should().Be("model_unreadable");
    }

    [Fact]
    public void A_refusal_names_the_model_that_was_asked_for()
    {
        var decision = new ModelPolicy(["claude-haiku-*"]).Check("claude-opus-5");

        decision.Reason.Should().Be("model_not_covered");
        decision.Model.Should().Be("claude-opus-5");
    }

    // ---- reading the provider's own usage figures ------------------------------------------------------

    [Fact]
    public void A_plain_answer_is_charged_what_the_provider_says_it_used()
    {
        var scanner = new UsageScanner();
        scanner.Feed(Encoding.UTF8.GetBytes(
            """{"id":"msg_1","usage":{"input_tokens":1200,"cache_creation_input_tokens":300,"cache_read_input_tokens":4000,"output_tokens":250}}"""));

        scanner.Tokens.Should().Be(1200 + 300 + 4000 + 250);
    }

    [Fact]
    public void A_streamed_answer_is_charged_its_final_figures_not_the_sum_of_every_event()
    {
        // message_start carries the input side and a first output count; each message_delta carries the
        // CUMULATIVE output so far. Adding them up would charge the output several times over.
        var scanner = new UsageScanner();
        foreach (var line in new[]
        {
            "event: message_start\ndata: {\"type\":\"message_start\",\"message\":{\"usage\":{\"input_tokens\":900,\"output_tokens\":1}}}\n\n",
            "event: message_delta\ndata: {\"type\":\"message_delta\",\"usage\":{\"output_tokens\":40}}\n\n",
            "event: message_delta\ndata: {\"type\":\"message_delta\",\"usage\":{\"output_tokens\":310}}\n\n",
        })
        {
            scanner.Feed(Encoding.UTF8.GetBytes(line));
        }

        scanner.Tokens.Should().Be(900 + 310);
    }

    [Fact]
    public void A_figure_split_across_two_chunks_of_the_stream_is_still_read_whole()
    {
        var bytes = Encoding.UTF8.GetBytes("""data: {"usage":{"input_tokens":123456,"output_tokens":7}}""");
        var scanner = new UsageScanner();

        // Cut in the middle of the number, which is where a naive per-chunk read goes wrong quietly.
        var cut = Encoding.UTF8.GetString(bytes).IndexOf("1234", StringComparison.Ordinal) + 3;
        scanner.Feed(bytes.AsSpan(0, cut));
        scanner.Feed(bytes.AsSpan(cut));

        scanner.Tokens.Should().Be(123456 + 7);
    }

    [Fact]
    public void An_answer_with_no_usage_in_it_reads_as_unknown_not_as_zero()
    {
        var scanner = new UsageScanner();
        scanner.Feed(Encoding.UTF8.GetBytes("""{"type":"error","error":{"type":"overloaded_error"}}"""));

        scanner.Tokens.Should().BeNull();
    }

    // ---- the budgets ------------------------------------------------------------------------------------

    private static (InMemoryTokenBudget Budget, TestTimeProvider Clock) Budget(
        long perUser = 1000, long organisation = 0, long unreadable = 500)
    {
        var options = new BrokerServiceOptions
        {
            QuotaTokens = perUser,
            QuotaWindow = TimeSpan.FromDays(1),
            OrganisationTokensPerDay = organisation,
            UnreadableUsageCharge = unreadable,
        };
        var clock = new TestTimeProvider(Now);
        return (new InMemoryTokenBudget(Options.Create(options), clock), clock);
    }

    [Fact]
    public void A_user_is_refused_once_what_they_have_used_reaches_their_budget_and_told_when_it_resets()
    {
        var (budget, _) = Budget(perUser: 1000);
        budget.Check("alice").Allowed.Should().BeTrue();

        budget.Charge("alice", 1000);

        var refused = budget.Check("alice");
        refused.Allowed.Should().BeFalse();
        refused.Limit.Should().Be("user");
        refused.ResetsAt.Should().Be(Now + TimeSpan.FromDays(1));
        budget.Check("bob").Allowed.Should().BeTrue("one user's spend is not another's");
    }

    [Fact]
    public void The_budget_comes_back_when_its_window_turns()
    {
        var (budget, clock) = Budget(perUser: 1000);
        budget.Charge("alice", 5000);
        budget.Check("alice").Allowed.Should().BeFalse();

        clock.Advance(TimeSpan.FromDays(1));

        budget.Check("alice").Allowed.Should().BeTrue();
    }

    [Fact]
    public void The_organisation_ceiling_refuses_everybody_whoever_spent_it()
    {
        // Ten users each inside their own limits are ten times the bill, and one leaked grant is a full one.
        var (budget, _) = Budget(perUser: 0, organisation: 3000);
        budget.Charge("alice", 2000);
        budget.Charge("bob", 1000);

        var refused = budget.Check("carol");

        refused.Allowed.Should().BeFalse();
        refused.Limit.Should().Be("organisation");
    }

    [Fact]
    public void An_answer_whose_usage_could_not_be_read_is_charged_the_stated_worst_case_never_nothing()
    {
        var (budget, _) = Budget(perUser: 1000, unreadable: 500);

        budget.Charge("alice", null);
        budget.Check("alice").Allowed.Should().BeTrue();
        budget.Charge("alice", null);

        budget.Check("alice").Allowed.Should().BeFalse("two unreadable answers at 500 each is the whole budget");
    }

    [Fact]
    public void A_budget_of_zero_means_no_token_limit_which_is_how_an_existing_deployment_keeps_running()
    {
        var (budget, _) = Budget(perUser: 0, organisation: 0);
        budget.Charge("alice", long.MaxValue / 4);

        budget.Check("alice").Allowed.Should().BeTrue();
    }

    // ---- around one forwarded request -------------------------------------------------------------------

    [Fact]
    public void A_model_the_grant_does_not_cover_is_refused_before_anything_is_spent_and_the_refusal_names_it()
    {
        var (budget, _) = Budget(perUser: 1000);
        var containment = new ForwardContainment(new ModelPolicy(["claude-haiku-*"]), budget, Quiet());

        var refused = containment.Before("alice", "claude-opus-5");

        refused!.Status.Should().Be(403);
        refused.Reason.Should().Be("model_not_covered");
        System.Text.Json.JsonSerializer.Serialize(refused.Body).Should().Contain("claude-opus-5");
        containment.Before("alice", "claude-haiku-4-5-20251001").Should().BeNull();
    }

    [Fact]
    public void A_spent_budget_is_refused_with_which_limit_and_when_it_resets()
    {
        var (budget, _) = Budget(perUser: 1000);
        var containment = new ForwardContainment(new ModelPolicy([]), budget, Quiet());
        budget.Charge("alice", 1000);

        var refused = containment.Before("alice", "claude-haiku-4-5-20251001");

        refused!.Status.Should().Be(429);
        var body = System.Text.Json.JsonSerializer.Serialize(refused.Body);
        body.Should().Contain("\"limit\":\"user\"").And.Contain("resets_at");
    }

    [Fact]
    public void Only_a_successful_answer_to_the_messages_path_is_charged()
    {
        var (budget, _) = Budget(perUser: 100_000);
        var containment = new ForwardContainment(new ModelPolicy([]), budget, Quiet());

        containment.After("alice", "/v1/messages", 200, Input(700));
        containment.After("alice", "/v1/messages", 429, Input(9_999));             // the provider refused: nothing was generated
        containment.After("alice", "/v1/messages/count_tokens", 200, Input(9_999)); // counts tokens, generates none
        containment.After("alice", "/v1/models", 200, null);                // unreadable, and still not a generation

        budget.UsedBy("alice").Should().Be(700);
    }

    [Fact]
    public void A_successful_answer_nobody_could_read_the_usage_of_is_charged_the_worst_case()
    {
        var (budget, _) = Budget(perUser: 100_000, unreadable: 500);
        var containment = new ForwardContainment(new ModelPolicy([]), budget, Quiet());

        containment.After("alice", "/v1/messages", 200, null);

        budget.UsedBy("alice").Should().Be(500);
    }

    private static TokenUsage Input(long tokens) => new(tokens, 0, 0, 0);

    /// <summary>A suspension policy that never suspends, for the tests that are about something else.</summary>
    private static Suspensions Quiet() =>
        new(Options.Create(new BrokerServiceOptions { SuspendAfterRefusals = 0 }), new TestTimeProvider(Now));

    // ---- refusals add up, and a suspended user is served nothing --------------------------

    [Fact]
    public void Every_way_a_user_can_be_refused_counts_toward_the_same_suspension()
    {
        // A wall a loop can walk around by changing which wall it hits is not a wall.
        var (budget, _) = Budget(perUser: 1);
        var suspensions = new Suspensions(
            Options.Create(new BrokerServiceOptions { SuspendAfterRefusals = 3, SuspensionWindow = TimeSpan.FromMinutes(10) }),
            new TestTimeProvider(Now));
        var containment = new ForwardContainment(new ModelPolicy(["claude-haiku-*"]), budget, suspensions);

        containment.Before("alice", "claude-opus-5").Should().NotBeNull();   // the model
        budget.Charge("alice", 10);
        containment.Before("alice", "claude-haiku-4-5").Should().NotBeNull(); // the budget
        containment.Refused("alice", "ratelimited");                          // the meter, from the endpoint

        suspensions.Of("alice").Should().NotBeNull("three refusals of three different kinds are still three");
    }

    [Fact]
    public void A_suspended_user_is_refused_before_the_model_or_the_budget_is_even_looked_at()
    {
        var (budget, _) = Budget(perUser: 1_000_000);
        var suspensions = new Suspensions(
            Options.Create(new BrokerServiceOptions { SuspendAfterRefusals = 1 }), new TestTimeProvider(Now));
        suspensions.Refused("alice", "anything");
        var containment = new ForwardContainment(new ModelPolicy([]), budget, suspensions);

        var refused = containment.Before("alice", "claude-haiku-4-5");

        refused!.Status.Should().Be(403);
        refused.Reason.Should().Be("suspended");
        containment.Before("bob", "claude-haiku-4-5").Should().BeNull("one user's suspension is not another's");
    }

    [Fact]
    public void The_same_refusal_is_what_the_mint_path_asks_for_so_a_new_token_cannot_be_had_either()
    {
        var suspensions = new Suspensions(
            Options.Create(new BrokerServiceOptions { SuspendAfterRefusals = 1 }), new TestTimeProvider(Now));
        suspensions.Refused("alice", "anything");
        var containment = new ForwardContainment(new ModelPolicy([]), Budget().Budget, suspensions);

        containment.Suspended("alice").Should().NotBeNull();
        containment.Suspended("bob").Should().BeNull();
    }

    [Fact]
    public void What_a_user_has_used_can_be_read_back()
    {
        var (budget, _) = Budget(perUser: 1000);
        budget.Charge("alice", 120);
        budget.Charge("alice", 30);

        budget.UsedBy("alice").Should().Be(150);
        budget.UsedBy("nobody").Should().Be(0);
    }
}
