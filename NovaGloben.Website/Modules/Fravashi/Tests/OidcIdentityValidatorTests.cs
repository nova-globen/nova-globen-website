using System.Net;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Identity;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// The generic OIDC identity kind: verify a proof against ANY standards-compliant issuer's userinfo endpoint
/// (a Stomme IdP, for one). Shapes here match a Stomme userinfo response:
/// <c>{"sub":"...","name":"ada","preferred_username":"ada","email":"ada@example.test"}</c>.
/// </summary>
public sealed class OidcIdentityValidatorTests
{
    private const string GoodToken = "idp-access-token-good";
    private const string Issuer = "https://idp.example.test";
    private const string UserInfo = "https://idp.example.test/connect/userinfo";
    private const string RealUserInfoBody =
        """{"sub":"2b1c0f3e-8a4d-4c6e-9f10-000000000001","name":"ada","preferred_username":"ada","email":"ada@example.test"}""";

    private static SsoIdentityValidator Build(StubIdpHandler handler, params TrustedIdentityProvider[] providers)
    {
        var options = new BrokerServiceOptions { Identity = new BrokerIdentityOptions { Providers = [.. providers] } };
        return new SsoIdentityValidator(new HttpClient(handler), Options.Create(options), NullLogger<SsoIdentityValidator>.Instance);
    }

    [Fact]
    public async Task Accepts_a_valid_token_and_returns_the_subject_with_its_claims()
    {
        var handler = new StubIdpHandler
        {
            Responder = (url, token) => url == UserInfo && token == GoodToken
                ? StubIdpHandler.Ok(RealUserInfoBody)
                : StubIdpHandler.Status(HttpStatusCode.Unauthorized),
        };
        var validator = Build(handler, new TrustedIdentityProvider
        {
            Kind = TrustedIdentityProvider.Oidc,
            UserInfoUrl = UserInfo,
        });

        var result = await validator.ValidateAsync("host", GoodToken, CancellationToken.None);

        result!.CanonicalId.Should().Be("oidc:2b1c0f3e-8a4d-4c6e-9f10-000000000001");
        result.Email.Should().Be("ada@example.test");
        result.Username.Should().Be("ada");
    }

    [Fact]
    public async Task Rejects_a_token_the_issuer_refuses()
    {
        var handler = new StubIdpHandler { Responder = (_, _) => StubIdpHandler.Status(HttpStatusCode.Unauthorized) };
        var validator = Build(handler, new TrustedIdentityProvider
        {
            Kind = TrustedIdentityProvider.Oidc,
            UserInfoUrl = UserInfo,
        });

        (await validator.ValidateAsync("host", "forged", CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task Discovers_the_userinfo_endpoint_from_the_issuer_and_caches_it()
    {
        var discoveryCalls = 0;
        var handler = new StubIdpHandler
        {
            Responder = (url, token) =>
            {
                if (url.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal))
                {
                    discoveryCalls++;
                    return StubIdpHandler.Ok($$"""{"issuer":"{{Issuer}}","userinfo_endpoint":"{{UserInfo}}"}""");
                }

                return url == UserInfo && token == GoodToken
                    ? StubIdpHandler.Ok(RealUserInfoBody)
                    : StubIdpHandler.Status(HttpStatusCode.Unauthorized);
            },
        };
        var validator = Build(handler, new TrustedIdentityProvider
        {
            Kind = TrustedIdentityProvider.Oidc,
            Issuer = Issuer,
        });

        (await validator.ValidateAsync("host", GoodToken, CancellationToken.None))!.CanonicalId
            .Should().Be("oidc:2b1c0f3e-8a4d-4c6e-9f10-000000000001");
        (await validator.ValidateAsync("host", GoodToken, CancellationToken.None))!.CanonicalId
            .Should().NotBeNull();

        discoveryCalls.Should().Be(1, "the issuer's metadata is fetched once and cached");
    }

    [Fact]
    public async Task An_unreachable_issuer_is_a_rejection_not_a_crash()
    {
        var handler = new StubIdpHandler
        {
            Responder = (url, _) => url.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal)
                ? StubIdpHandler.Status(HttpStatusCode.ServiceUnavailable)
                : StubIdpHandler.Ok(RealUserInfoBody),
        };
        var validator = Build(handler, new TrustedIdentityProvider
        {
            Kind = TrustedIdentityProvider.Oidc,
            Issuer = Issuer,
        });

        (await validator.ValidateAsync("host", GoodToken, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task A_userinfo_response_without_a_subject_is_rejected()
    {
        var handler = new StubIdpHandler { Responder = (_, _) => StubIdpHandler.Ok("""{"name":"no subject here"}""") };
        var validator = Build(handler, new TrustedIdentityProvider
        {
            Kind = TrustedIdentityProvider.Oidc,
            UserInfoUrl = UserInfo,
        });

        (await validator.ValidateAsync("host", GoodToken, CancellationToken.None)).Should().BeNull();
    }
}
