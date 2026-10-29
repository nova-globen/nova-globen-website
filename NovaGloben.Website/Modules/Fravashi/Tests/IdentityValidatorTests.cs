using Fravashi.Broker.Configuration;
using Fravashi.Broker.Identity;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class IdentityValidatorTests
{
    private static ConfigIdentityValidator Build(string? requiredProof) =>
        new(Options.Create(new BrokerServiceOptions { StubIdentityProof = requiredProof }));

    [Fact]
    public async Task Rejects_an_empty_user_id()
    {
        var result = await Build(null).ValidateAsync("", null, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Accepts_any_user_when_no_proof_is_required()
    {
        var result = await Build(null).ValidateAsync("alice", null, CancellationToken.None);
        result!.CanonicalId.Should().Be("alice");
    }

    [Fact]
    public async Task Requires_a_matching_proof_when_configured()
    {
        var validator = Build("expected-proof");

        (await validator.ValidateAsync("alice", "wrong", CancellationToken.None)).Should().BeNull();
        (await validator.ValidateAsync("alice", "expected-proof", CancellationToken.None))!.CanonicalId.Should().Be("alice");
    }
}
