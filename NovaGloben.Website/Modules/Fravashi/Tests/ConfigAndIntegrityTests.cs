using Fravashi.Broker.Configuration;
using Fravashi.Broker.Integrity;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class ConfigAndIntegrityTests
{
    private const string GoodOrgKey = "sk-ant-TESTKEY-do-not-use";
    private const string GoodSigning = "this-is-a-32-byte-signing-secret!!";

    private static Func<string, string?> Env(string? orgKey, string? signing) => name => name switch
    {
        BrokerSecrets.OrgKeyEnvVar => orgKey,
        BrokerSecrets.SigningSecretEnvVar => signing,
        _ => null,
    };

    [Fact]
    public void Missing_org_key_refuses_to_start()
    {
        var act = () => BrokerSecrets.FromEnvironment(Env(orgKey: null, signing: GoodSigning));
        act.Should().Throw<BrokerConfigurationException>().WithMessage("*org key*");
    }

    [Fact]
    public void Missing_signing_secret_refuses_to_start()
    {
        var act = () => BrokerSecrets.FromEnvironment(Env(orgKey: GoodOrgKey, signing: null));
        act.Should().Throw<BrokerConfigurationException>().WithMessage("*signing secret*");
    }

    [Fact]
    public void Weak_signing_secret_refuses_to_start()
    {
        var act = () => BrokerSecrets.FromEnvironment(Env(orgKey: GoodOrgKey, signing: "too-short"));
        act.Should().Throw<BrokerConfigurationException>().WithMessage("*too weak*");
    }

    [Fact]
    public void Both_secrets_present_loads()
    {
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        secrets.OrgApiKey.Should().Be(GoodOrgKey);
        secrets.SigningKey.Length.Should().BeGreaterThanOrEqualTo(BrokerSecrets.MinSigningSecretLength);
    }

    [Fact]
    public void Coherent_config_passes_the_integrity_check()
    {
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        var result = BrokerIntegrityCheck.Evaluate(secrets, new BrokerServiceOptions());
        result.Ok.Should().BeTrue();
        result.Problems.Should().BeEmpty();
    }

    [Fact]
    public void A_real_idp_with_no_mint_allowlist_fails_closed()
    {
        // Minting is deny-by-default, so a broker trusting a real IdP without an
        // allowlist is an incoherent security config, never an implicit allow-all.
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        var options = new BrokerServiceOptions
        {
            Identity = new Fravashi.Broker.Identity.BrokerIdentityOptions
            {
                Providers = [new Fravashi.Broker.Identity.TrustedIdentityProvider
                {
                    Kind = Fravashi.Broker.Identity.TrustedIdentityProvider.Oidc,
                    Issuer = "https://idp.example.test",
                }],
            },
        };

        var result = BrokerIntegrityCheck.Evaluate(secrets, options);

        result.Ok.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Contains("allowlist", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_real_idp_with_an_allowlist_is_coherent()
    {
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        var options = new BrokerServiceOptions
        {
            Identity = new Fravashi.Broker.Identity.BrokerIdentityOptions
            {
                Providers = [new Fravashi.Broker.Identity.TrustedIdentityProvider
                {
                    Kind = Fravashi.Broker.Identity.TrustedIdentityProvider.Oidc,
                    Issuer = "https://idp.example.test",
                }],
                Allowlist = ["ada@example.com"],
            },
        };

        BrokerIntegrityCheck.Evaluate(secrets, options).Ok.Should().BeTrue();
    }

    [Fact]
    public void The_dev_stub_path_needs_no_allowlist()
    {
        // No providers configured = the dev/test stub validator; the allowlist rule targets real IdPs only.
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));

        BrokerIntegrityCheck.Evaluate(secrets, new BrokerServiceOptions()).Ok.Should().BeTrue();
    }

    [Fact]
    public void Non_https_provider_url_fails_closed()
    {
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        var options = new BrokerServiceOptions { ProviderBaseUrl = "http://api.anthropic.com" };
        var result = BrokerIntegrityCheck.Evaluate(secrets, options);
        result.Ok.Should().BeFalse();
        result.Problems.Should().Contain(p => p.Contains("https"));
    }

    [Fact]
    public void Non_positive_ttl_fails_closed()
    {
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        var options = new BrokerServiceOptions { TokenTtl = TimeSpan.Zero };
        BrokerIntegrityCheck.Evaluate(secrets, options).Ok.Should().BeFalse();
    }

    [Fact]
    public void Non_positive_rate_and_quota_fail_closed()
    {
        var secrets = BrokerSecrets.FromEnvironment(Env(GoodOrgKey, GoodSigning));
        BrokerIntegrityCheck.Evaluate(secrets, new BrokerServiceOptions { RateLimitRequests = 0 }).Ok.Should().BeFalse();
        BrokerIntegrityCheck.Evaluate(secrets, new BrokerServiceOptions { QuotaRequests = 0 }).Ok.Should().BeFalse();
    }
}
