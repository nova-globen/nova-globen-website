using Fravashi.Broker.Endpoints;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// The mint response reports the IdP-DERIVED subject, so a client shows who it is actually signed in as rather
/// than echoing the id it guessed when it asked (which is "broker-user" before the first mint resolves).
/// </summary>
public class MintSubjectTests
{
    [Fact]
    public void The_response_carries_the_canonical_subject()
    {
        var response = new MintResponse("tok", "jti-1", DateTimeOffset.UtcNow.AddHours(1), "oidc:2b1c0f3e");

        response.Subject.Should().Be("oidc:2b1c0f3e");
    }

    [Fact]
    public void The_subject_is_optional_so_an_older_client_contract_still_binds()
    {
        var response = new MintResponse("tok", "jti-1", DateTimeOffset.UtcNow.AddHours(1));

        response.Subject.Should().BeNull();
    }
}
