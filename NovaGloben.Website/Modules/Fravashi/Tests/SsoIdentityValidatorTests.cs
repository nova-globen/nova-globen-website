using System.Net;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Identity;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class SsoIdentityValidatorTests
{
    private const string GoodToken = "idp-access-token-good";

    private static SsoIdentityValidator Build(StubIdpHandler handler, params TrustedIdentityProvider[] providers)
    {
        var options = new BrokerServiceOptions { Identity = new BrokerIdentityOptions { Providers = [.. providers] } };
        return new SsoIdentityValidator(new HttpClient(handler), Options.Create(options), NullLogger<SsoIdentityValidator>.Instance);
    }

    [Fact]
    public async Task Rejects_a_missing_proof_without_calling_the_provider()
    {
        var handler = new StubIdpHandler();
        var validator = Build(handler, new TrustedIdentityProvider { Kind = TrustedIdentityProvider.GitHub });

        var result = await validator.ValidateAsync("alice", null, CancellationToken.None);

        result.Should().BeNull();
        handler.Calls.Should().Be(0, "an absent proof is rejected before any network call");
    }

    [Fact]
    public async Task Accepts_a_valid_GitHub_token_and_returns_the_canonical_id()
    {
        var handler = new StubIdpHandler
        {
            Responder = (url, token) => url.Contains("api.github.com") && token == GoodToken
                ? StubIdpHandler.Ok("{\"id\":4242,\"login\":\"octocat\"}")
                : StubIdpHandler.Status(HttpStatusCode.Unauthorized),
        };
        var validator = Build(handler, new TrustedIdentityProvider { Kind = TrustedIdentityProvider.GitHub });

        // The IdP-derived id wins over the client-claimed userId (a client cannot mint for someone else).
        var result = await validator.ValidateAsync("someone-else", GoodToken, CancellationToken.None);

        result!.CanonicalId.Should().Be("github:4242");
    }

    [Fact]
    public async Task Rejects_an_invalid_or_expired_token()
    {
        var handler = new StubIdpHandler { Responder = (_, _) => StubIdpHandler.Status(HttpStatusCode.Unauthorized) };
        var validator = Build(handler, new TrustedIdentityProvider { Kind = TrustedIdentityProvider.GitHub });

        var result = await validator.ValidateAsync("alice", "expired-token", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Accepts_a_valid_Entra_token_and_returns_the_object_id()
    {
        var handler = new StubIdpHandler
        {
            Responder = (url, _) => url.Contains("graph.microsoft.com")
                ? StubIdpHandler.Ok("{\"id\":\"9f1e-oid\",\"userPrincipalName\":\"alice@contoso.com\"}")
                : StubIdpHandler.Status(HttpStatusCode.Unauthorized),
        };
        var validator = Build(handler, new TrustedIdentityProvider { Kind = TrustedIdentityProvider.Entra });

        var result = await validator.ValidateAsync("alice", GoodToken, CancellationToken.None);

        result!.CanonicalId.Should().Be("entra:9f1e-oid");
    }

    [Fact]
    public async Task Tries_each_trusted_provider_until_one_accepts()
    {
        // GitHub rejects; Entra accepts. The validator should fall through to Entra.
        var handler = new StubIdpHandler
        {
            Responder = (url, _) => url.Contains("graph.microsoft.com")
                ? StubIdpHandler.Ok("{\"id\":\"oid-2\"}")
                : StubIdpHandler.Status(HttpStatusCode.Unauthorized),
        };
        var validator = Build(handler,
            new TrustedIdentityProvider { Kind = TrustedIdentityProvider.GitHub },
            new TrustedIdentityProvider { Kind = TrustedIdentityProvider.Entra });

        var result = await validator.ValidateAsync("alice", GoodToken, CancellationToken.None);

        result!.CanonicalId.Should().Be("entra:oid-2");
        handler.Calls.Should().Be(2, "GitHub was tried first, then Entra");
    }

    [Fact]
    public async Task Honors_a_userinfo_url_override_for_a_stub_or_enterprise_idp()
    {
        string? seenUrl = null;
        var handler = new StubIdpHandler
        {
            Responder = (url, _) =>
            {
                seenUrl = url;
                return StubIdpHandler.Ok("{\"id\":7}");
            },
        };
        var validator = Build(handler, new TrustedIdentityProvider
        {
            Kind = TrustedIdentityProvider.GitHub,
            UserInfoUrl = "https://ghe.internal.example/api/v3/user",
        });

        var result = await validator.ValidateAsync("alice", GoodToken, CancellationToken.None);

        result!.CanonicalId.Should().Be("github:7");
        seenUrl.Should().Be("https://ghe.internal.example/api/v3/user");
    }

}
