using Fravashi.Broker.Forwarding;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>A provider that cannot be reached or does not answer in time is a gateway error, not an unhandled 500.</summary>
public sealed class ForwardFailureTests
{
    [Fact]
    public void An_unreachable_provider_is_a_502() =>
        ForwardFailure.For(new HttpRequestException("refused"), clientGone: false).Should().Be((502, ForwardFailure.Unreachable));

    [Fact]
    public void A_provider_that_does_not_answer_in_time_is_a_504() =>
        ForwardFailure.For(new TaskCanceledException("timeout"), clientGone: false).Should().Be((504, ForwardFailure.Timeout));

    [Fact]
    public void A_client_that_hung_up_is_not_answered() =>
        ForwardFailure.For(new TaskCanceledException("gone"), clientGone: true).Should().BeNull();

    [Fact]
    public void Anything_else_is_left_to_fail_as_it_would() =>
        ForwardFailure.For(new InvalidOperationException(), clientGone: false).Should().BeNull();
}