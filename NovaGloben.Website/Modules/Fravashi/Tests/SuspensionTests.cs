using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// The two clauses metering alone cannot cover: a user who keeps hitting a wall is stopped rather
/// than refused one request at a time for ever, and an operator can read what happened and lift it.
/// </summary>
public sealed class SuspensionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 22, 12, 0, 0, TimeSpan.Zero);

    private static (Suspensions Suspensions, TestTimeProvider Clock) Build(int after = 3, TimeSpan? window = null)
    {
        var options = new BrokerServiceOptions
        {
            SuspendAfterRefusals = after,
            SuspensionWindow = window ?? TimeSpan.FromMinutes(10),
        };
        var clock = new TestTimeProvider(Now);
        return (new Suspensions(Options.Create(options), clock), clock);
    }

    [Fact]
    public void A_user_who_is_refused_below_the_line_is_not_suspended()
    {
        var (suspensions, _) = Build(after: 3);

        suspensions.Refused("alice", "model_not_covered");
        suspensions.Refused("alice", "model_not_covered");

        suspensions.Of("alice").Should().BeNull();
    }

    [Fact]
    public void Enough_refusals_inside_the_window_suspends_the_user_and_says_why()
    {
        var (suspensions, _) = Build(after: 3);

        for (var i = 0; i < 3; i++)
        {
            suspensions.Refused("alice", "model_not_covered");
        }

        var held = suspensions.Of("alice");
        held.Should().NotBeNull();
        held!.Refusals.Should().Be(3);
        held.Reason.Should().Be("model_not_covered", "the last thing they were refused for is what an operator needs to read");
        suspensions.Of("bob").Should().BeNull("one user's refusals are not another's");
    }

    [Fact]
    public void Refusals_spread_wider_than_the_window_never_add_up()
    {
        var (suspensions, clock) = Build(after: 3, window: TimeSpan.FromMinutes(10));

        for (var i = 0; i < 5; i++)
        {
            suspensions.Refused("alice", "token_budget_spent_user");
            clock.Advance(TimeSpan.FromMinutes(11));
        }

        suspensions.Of("alice").Should().BeNull("a slow trickle of refusals is somebody working, not somebody hammering");
    }

    [Fact]
    public void A_suspension_does_not_lift_itself_when_the_window_passes()
    {
        // The window bounds how fast refusals must arrive to earn a suspension, not how long one lasts. A
        // suspension that expired on its own would let the same loop start again unattended.
        var (suspensions, clock) = Build(after: 2, window: TimeSpan.FromMinutes(10));
        suspensions.Refused("alice", "no");
        suspensions.Refused("alice", "no");

        clock.Advance(TimeSpan.FromHours(6));

        suspensions.Of("alice").Should().NotBeNull();
    }

    [Fact]
    public void An_operator_lifts_it_and_the_count_starts_again()
    {
        var (suspensions, _) = Build(after: 2);
        suspensions.Refused("alice", "no");
        suspensions.Refused("alice", "no");

        suspensions.Lift("alice").Should().BeTrue();

        suspensions.Of("alice").Should().BeNull();
        suspensions.Refused("alice", "no");
        suspensions.Of("alice").Should().BeNull("the refusals that earned the lifted suspension are not counted again");
    }

    [Fact]
    public void Lifting_a_suspension_nobody_has_is_not_an_error_and_says_nothing_happened() =>
        Build().Suspensions.Lift("nobody").Should().BeFalse();

    [Fact]
    public void Zero_means_nobody_is_ever_suspended_which_is_what_a_deployment_from_before_this_keeps()
    {
        var (suspensions, _) = Build(after: 0);

        for (var i = 0; i < 50; i++)
        {
            suspensions.Refused("alice", "no");
        }

        suspensions.Of("alice").Should().BeNull();
    }

    [Fact]
    public void An_allowed_request_does_not_clear_what_a_user_has_been_refused()
    {
        // Suspension is about a pattern of refusals; letting one success reset it would make a loop that
        // alternates between allowed and refused calls invisible.
        var (suspensions, _) = Build(after: 3);
        suspensions.Refused("alice", "no");
        suspensions.Refused("alice", "no");

        suspensions.Refused("alice", "no");

        suspensions.Of("alice").Should().NotBeNull();
    }

    [Fact]
    public void What_a_user_has_been_refused_can_be_read_without_suspending_them()
    {
        var (suspensions, _) = Build(after: 10);
        suspensions.Refused("alice", "model_not_covered");
        suspensions.Refused("alice", "token_budget_spent_user");

        var seen = suspensions.RefusalsOf("alice");

        seen.Should().Be(2);
        suspensions.RefusalsOf("nobody").Should().Be(0);
    }
}
