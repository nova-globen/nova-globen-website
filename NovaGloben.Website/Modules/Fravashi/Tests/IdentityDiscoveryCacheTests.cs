using System.Net;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Identity;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// The validator is a typed HTTP client, so the container builds a NEW one for every mint. These tests build a
/// new one per mint too, which is the only way to see what production sees: with the discovery cache as a field
/// of the validator, every mint ran discovery again and nothing said so.
/// </summary>
public sealed class IdentityDiscoveryCacheTests
{
    private const string Issuer = "https://idp.example.test";
    private const string Discovery = "https://idp.example.test/.well-known/openid-configuration";
    private const string UserInfo = "https://idp.example.test/connect/userinfo";
    private const string Token = "idp-access-token-good";

    private static readonly BrokerServiceOptions Options = new()
    {
        Identity = new BrokerIdentityOptions
        {
            Providers = [new TrustedIdentityProvider { Kind = TrustedIdentityProvider.Oidc, Issuer = Issuer }],
        },
    };

    private static StubIdpHandler Idp(List<string> asked, bool discoveryUp = true) => new()
    {
        Responder = (url, token) =>
        {
            asked.Add(url);
            if (url == Discovery)
            {
                return discoveryUp
                    ? StubIdpHandler.Ok($$"""{"issuer":"{{Issuer}}","userinfo_endpoint":"{{UserInfo}}"}""")
                    : StubIdpHandler.Status(HttpStatusCode.ServiceUnavailable);
            }

            return url == UserInfo && token == Token
                ? StubIdpHandler.Ok("""{"sub":"s1","preferred_username":"host"}""")
                : StubIdpHandler.Status(HttpStatusCode.Unauthorized);
        },
    };

    private static SsoIdentityValidator NewValidator(StubIdpHandler idp, UserInfoDiscoveryCache cache) =>
        new(new HttpClient(idp), Microsoft.Extensions.Options.Options.Create(Options), NullLogger<SsoIdentityValidator>.Instance, cache);

    [Fact]
    public async Task Discovery_runs_once_for_the_process_and_not_once_for_every_mint()
    {
        var asked = new List<string>();
        var idp = Idp(asked);
        var cache = new UserInfoDiscoveryCache();

        for (var mint = 0; mint < 3; mint++)
        {
            var identity = await NewValidator(idp, cache).ValidateAsync("host", Token, CancellationToken.None);
            identity!.CanonicalId.Should().Be("oidc:s1");
        }

        asked.Count(url => url == Discovery).Should().Be(1);
        asked.Count(url => url == UserInfo).Should().Be(3);
    }

    [Fact]
    public async Task A_warmed_broker_does_not_discover_on_its_first_mint()
    {
        var asked = new List<string>();
        var idp = Idp(asked);
        var cache = new UserInfoDiscoveryCache();

        (await NewValidator(idp, cache).WarmAsync(CancellationToken.None)).Should().Be(1);
        asked.Clear();

        await NewValidator(idp, cache).ValidateAsync("host", Token, CancellationToken.None);

        asked.Should().Equal(UserInfo);
    }

    [Fact]
    public async Task An_idp_that_is_down_at_start_does_not_stop_the_broker_and_is_asked_again_later()
    {
        var asked = new List<string>();
        var cache = new UserInfoDiscoveryCache();

        (await NewValidator(Idp(asked, discoveryUp: false), cache).WarmAsync(CancellationToken.None)).Should().Be(0);
        cache.Endpoints.Should().BeEmpty("a failure is never cached as an answer");

        var identity = await NewValidator(Idp(asked), cache).ValidateAsync("host", Token, CancellationToken.None);

        identity!.CanonicalId.Should().Be("oidc:s1");
    }
}
