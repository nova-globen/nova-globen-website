using Fravashi.Broker.Configuration;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// The <c>_FILE</c> secret convention (container secret stores): the variable names a file whose content is the
/// secret, so the value never lands in the process environment or an image layer.
/// </summary>
public class BrokerSecretsFileTests
{
    private const string StrongSecret = "0123456789abcdef0123456789abcdef";

    private static Func<string, string?> Env(params (string Key, string? Value)[] pairs)
    {
        var map = pairs.ToDictionary(p => p.Key, p => p.Value);
        return key => map.GetValueOrDefault(key);
    }

    [Fact]
    public void Secrets_load_from_the_named_files()
    {
        var secrets = BrokerSecrets.FromEnvironment(
            Env((BrokerSecrets.OrgKeyEnvVar + BrokerSecrets.FileSuffix, "/run/secrets/org"),
                (BrokerSecrets.SigningSecretEnvVar + BrokerSecrets.FileSuffix, "/run/secrets/signing")),
            path => path switch
            {
                "/run/secrets/org" => "sk-from-file\n",          // trailing newline: how a mounted secret arrives
                "/run/secrets/signing" => StrongSecret + "\n",
                _ => throw new FileNotFoundException(path),
            });

        secrets.OrgApiKey.Should().Be("sk-from-file");
        secrets.SigningKey.Should().HaveCount(StrongSecret.Length);
    }

    [Fact]
    public void The_direct_variable_wins_over_the_file()
    {
        var secrets = BrokerSecrets.FromEnvironment(
            Env((BrokerSecrets.OrgKeyEnvVar, "sk-direct"),
                (BrokerSecrets.OrgKeyEnvVar + BrokerSecrets.FileSuffix, "/run/secrets/org"),
                (BrokerSecrets.SigningSecretEnvVar, StrongSecret)),
            _ => "sk-from-file");

        secrets.OrgApiKey.Should().Be("sk-direct");
    }

    [Fact]
    public void An_unreadable_secret_file_fails_closed_without_naming_its_content()
    {
        var act = () => BrokerSecrets.FromEnvironment(
            Env((BrokerSecrets.OrgKeyEnvVar + BrokerSecrets.FileSuffix, "/run/secrets/missing"),
                (BrokerSecrets.SigningSecretEnvVar, StrongSecret)),
            _ => throw new FileNotFoundException("no such file"));

        act.Should().Throw<BrokerConfigurationException>()
            .Which.Message.Should().Contain("fail closed").And.NotContain("no such file");
    }

    [Fact]
    public void Neither_variable_set_still_fails_closed()
    {
        var act = () => BrokerSecrets.FromEnvironment(Env(), _ => "unused");

        act.Should().Throw<BrokerConfigurationException>();
    }
}
