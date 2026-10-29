using Fravashi.Broker.Configuration;

namespace Fravashi.Broker.Tests;

/// <summary>The broker as a module of the host: where it answers, what the provider sees, and when it refuses to serve.</summary>
public sealed class ModuleRoutesTests
{
    private const string Signing = "0123456789abcdef0123456789abcdef-signing";

    [Theory]
    [InlineData("/fravashi/api/v1/messages", "/v1/messages")]
    [InlineData("/fravashi/api/v1/messages/count_tokens", "/v1/messages/count_tokens")]
    [InlineData("/FRAVASHI/API/v1/models", "/v1/models")]
    [InlineData("/fravashi/api", null)]
    [InlineData("/fravashi/apiv1/messages", null)]
    [InlineData("/v1/messages", null)]
    [InlineData(null, null)]
    public void The_provider_never_sees_the_hosts_prefix(string? requestPath, string? upstream) =>
        BrokerRoutes.UpstreamPath(requestPath).Should().Be(upstream);

    [Theory]
    [InlineData("release-manifest.json", true)]
    [InlineData("release-manifest.json.sig", true)]
    [InlineData("fravashi-setup-x64.exe", true)]
    [InlineData("..", false)]
    [InlineData(".hidden", false)]
    [InlineData("a/b.json", false)]
    [InlineData("a%2Fb.json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void The_update_channel_answers_single_file_names_only(string? name, bool accepted) =>
        BrokerRoutes.IsUpdateFileName(name).Should().Be(accepted);

    private const string Releases = "https://github.com/nova-globen/fravashi/releases";

    [Theory]
    // updates/<file>: the newest release (the launcher's form)
    [InlineData(null, "release-manifest.json", Releases + "/latest/download/release-manifest.json")]
    [InlineData(null, "release-manifest.json.sig", Releases + "/latest/download/release-manifest.json.sig")]
    // updates/latest/<file>: the newest release (the web setup's form)
    [InlineData("latest", "SHA256SUMS.txt", Releases + "/latest/download/SHA256SUMS.txt")]
    // updates/<version>/<file>: the release tagged v<version>
    [InlineData("1.4.0", "release-manifest.json", Releases + "/download/v1.4.0/release-manifest.json")]
    [InlineData("0.9.102", "release-manifest.json.sig", Releases + "/download/v0.9.102/release-manifest.json.sig")]
    [InlineData("2.0.0-rc.1", "SHA256SUMS.txt", Releases + "/download/v2.0.0-rc.1/SHA256SUMS.txt")]
    [InlineData("10", "release-manifest.json", Releases + "/download/v10/release-manifest.json")]
    public void Each_update_form_redirects_to_its_release_asset(string? version, string name, string target) =>
        BrokerRoutes.UpdateTarget(null, version, name).Should().Be(target);

    [Theory]
    [InlineData("v1.4.0")]
    [InlineData("1.4.0-")]
    [InlineData("1..4")]
    [InlineData(".1.4")]
    [InlineData("1.4.0+build")]
    [InlineData("1.4.0-rc_1")]
    [InlineData("..")]
    [InlineData("Latest")]
    [InlineData("")]
    public void A_version_that_is_not_digits_and_dots_is_refused(string version)
    {
        BrokerRoutes.IsUpdateVersion(version).Should().BeFalse();
        BrokerRoutes.UpdateTarget(null, version, "release-manifest.json").Should().BeNull();
    }

    [Theory]
    [InlineData(null, "../secrets")]
    [InlineData("1.4.0", "../release-manifest.json")]
    [InlineData("latest", "a/b.json")]
    [InlineData("latest", "")]
    public void A_file_that_is_not_one_plain_name_is_refused(string? version, string name) =>
        BrokerRoutes.UpdateTarget(null, version, name).Should().BeNull();

    [Fact]
    public void A_configured_releases_address_is_used_and_must_be_https()
    {
        BrokerRoutes.UpdateTarget("https://github.com/example-org/fravashi/releases/", "1.0.0", "release-manifest.json")
            .Should().Be("https://github.com/example-org/fravashi/releases/download/v1.0.0/release-manifest.json");
        BrokerRoutes.UpdateTarget("http://example.invalid/releases", null, "release-manifest.json").Should().BeNull();
    }

    [Fact]
    public void Without_its_secrets_the_broker_does_not_serve_and_says_why()
    {
        var state = new BrokerState(_ => null, new BrokerServiceOptions());

        state.Ready.Should().BeFalse();
        state.Secrets.Should().BeNull();
        state.Problems.Should().ContainSingle().Which.Should().Contain(BrokerSecrets.OrgKeyEnvVar);
    }

    [Fact]
    public void A_weak_signing_secret_keeps_it_from_serving()
    {
        var state = new BrokerState(
            name => name switch { BrokerSecrets.OrgKeyEnvVar => "org-key", BrokerSecrets.SigningSecretEnvVar => "short", _ => null },
            new BrokerServiceOptions());

        state.Ready.Should().BeFalse();
        state.Problems.Should().ContainSingle().Which.Should().Contain("too weak");
    }

    [Fact]
    public void With_coherent_secrets_it_serves()
    {
        var state = new BrokerState(
            name => name switch { BrokerSecrets.OrgKeyEnvVar => "org-key", BrokerSecrets.SigningSecretEnvVar => Signing, _ => null },
            new BrokerServiceOptions());

        state.Ready.Should().BeTrue();
        state.Problems.Should().BeEmpty();
    }
}
