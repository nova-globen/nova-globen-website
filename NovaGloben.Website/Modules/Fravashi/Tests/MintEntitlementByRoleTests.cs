using System.Net;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Identity;
using Fravashi.Broker.Integrity;
using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// Entitlement by a role the IdP vouches for (<c>Broker:Identity:RequiredRole</c>): the second arm of the
/// deny-by-default mint rule, so who may spend the budget is managed in the IdP's own admin UI and the broker
/// keeps no state. Every test here is about one thing: the new arm must never turn absence into permission.
/// </summary>
public sealed class MintEntitlementByRoleTests
{
    private const string UserInfo = "https://idp.example.test/connect/userinfo";
    private const string Token = "idp-access-token-good";

    private static ValidatedIdentity Ada(params string[] roles) =>
        new("oidc:2b1c0f3e-8a4d-4c6e-9f10-000000000001", "ada@example.com", "ada") { Roles = roles };

    [Fact]
    public void The_holder_of_the_required_role_may_mint_without_being_listed()
    {
        var options = new BrokerIdentityOptions { RequiredRole = "deck-agent-user" };

        MintPolicy.Refusal(Ada("staff", "deck-agent-user"), options).Should().BeNull();
    }

    [Fact]
    public void Someone_without_the_role_is_refused_as_not_entitled()
    {
        var options = new BrokerIdentityOptions { RequiredRole = "deck-agent-user" };

        MintPolicy.Refusal(Ada("staff"), options).Should().Be("not_entitled");
    }

    [Fact]
    public void An_answer_that_carried_no_roles_at_all_is_refused()
    {
        // What a Stomme that does not emit roles yet, or a studio that did not ask for the scope, looks like.
        var options = new BrokerIdentityOptions { RequiredRole = "deck-agent-user" };

        MintPolicy.Refusal(Ada(), options).Should().Be("not_entitled");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_required_role_entitles_nobody(string? blank)
    {
        // The trap: a blank setting compared against a blank or whitespace role must not come out equal.
        MintPolicy.HoldsRole(Ada("", "   ", "staff"), blank).Should().BeFalse();
    }

    [Theory]
    [InlineData("studio-agent")]        // a prefix of the role
    [InlineData("studio-agent-user-2")] // the role is a prefix of it
    [InlineData("*")]
    public void A_role_is_matched_whole_and_never_as_a_pattern(string held)
    {
        MintPolicy.HoldsRole(Ada(held), "studio-agent-user").Should().BeFalse();
    }

    [Fact]
    public void Role_names_match_without_regard_to_case()
    {
        MintPolicy.HoldsRole(Ada("Studio-Agent-User"), " studio-agent-user ").Should().BeTrue();
    }

    [Fact]
    public void The_allowlist_still_admits_a_listed_user_who_lacks_the_role()
    {
        var options = new BrokerIdentityOptions { RequiredRole = "deck-agent-user", Allowlist = ["ada@example.com"] };

        MintPolicy.Refusal(Ada(), options).Should().BeNull();
    }

    [Fact]
    public void Without_a_required_role_the_refusal_keeps_its_old_name()
    {
        var options = new BrokerIdentityOptions { Allowlist = ["bob@example.com"] };

        MintPolicy.Refusal(Ada("deck-agent-user"), options).Should().Be("not_allowlisted");
    }

    [Theory]
    [InlineData("""{"sub":"s1","role":"deck-agent-user"}""", new[] { "deck-agent-user" })]
    [InlineData("""{"sub":"s1","role":["staff","deck-agent-user"]}""", new[] { "staff", "deck-agent-user" })]
    [InlineData("""{"sub":"s1","roles":["staff"]}""", new[] { "staff" })]
    [InlineData("""{"sub":"s1","roles":"staff"}""", new[] { "staff" })]
    [InlineData("""{"sub":"s1"}""", new string[0])]
    [InlineData("""{"sub":"s1","role":{"name":"deck-agent-user"}}""", new string[0])]
    [InlineData("""{"sub":"s1","roles":[["deck-agent-user"],7,null,"  "]}""", new string[0])]
    public async Task Roles_are_read_from_the_userinfo_answer_in_either_shape(string body, string[] expected)
    {
        var handler = new StubIdpHandler
        {
            Responder = (url, token) => url == UserInfo && token == Token
                ? StubIdpHandler.Ok(body)
                : StubIdpHandler.Status(HttpStatusCode.Unauthorized),
        };
        var options = new BrokerServiceOptions
        {
            Identity = new BrokerIdentityOptions
            {
                Providers = [new TrustedIdentityProvider { Kind = TrustedIdentityProvider.Oidc, UserInfoUrl = UserInfo }],
            },
        };
        var validator = new SsoIdentityValidator(
            new HttpClient(handler), Options.Create(options), NullLogger<SsoIdentityValidator>.Instance);

        var identity = await validator.ValidateAsync("ada", Token, CancellationToken.None);

        identity!.Roles.Should().Equal(expected);
    }

    [Fact]
    public void A_real_idp_with_only_a_required_role_is_a_coherent_configuration()
    {
        var options = WithProvider(TrustedIdentityProvider.Oidc);
        options.Identity.RequiredRole = "deck-agent-user";

        BrokerIntegrityCheck.Evaluate(Secrets(), options).Ok.Should().BeTrue();
    }

    [Fact]
    public void A_blank_required_role_does_not_stand_in_for_an_allowlist()
    {
        var options = WithProvider(TrustedIdentityProvider.Oidc);
        options.Identity.RequiredRole = "  ";

        var result = BrokerIntegrityCheck.Evaluate(Secrets(), options);

        result.Ok.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Contains("Broker:Identity:RequiredRole", StringComparison.Ordinal));
    }

    [Fact]
    public void A_required_role_nobody_could_ever_hold_refuses_to_start()
    {
        // GitHub's userinfo is never read for roles, so this broker would start cleanly and refuse everyone.
        var options = WithProvider(TrustedIdentityProvider.GitHub);
        options.Identity.RequiredRole = "deck-agent-user";

        var result = BrokerIntegrityCheck.Evaluate(Secrets(), options);

        result.Ok.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Contains("Nobody could mint", StringComparison.Ordinal));
    }

    private static BrokerServiceOptions WithProvider(string kind) => new()
    {
        Identity = new BrokerIdentityOptions
        {
            Providers = [new TrustedIdentityProvider { Kind = kind, Issuer = "https://idp.example.test" }],
        },
    };

    private static BrokerSecrets Secrets()
    {
        var env = new Dictionary<string, string?>
        {
            [BrokerSecrets.OrgKeyEnvVar] = "sk-ant-org-key-0123456789abcdef",
            [BrokerSecrets.SigningSecretEnvVar] = "signing-secret-0123456789abcdef0123456789abcdef",
        };
        return BrokerSecrets.FromEnvironment(name => env.GetValueOrDefault(name));
    }
}
