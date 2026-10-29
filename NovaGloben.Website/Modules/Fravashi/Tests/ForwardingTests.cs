using System.Net;
using System.Text;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Forwarding;
using AwesomeAssertions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class ForwardingTests
{
    private const string OrgKey = "sk-ant-TESTKEY-do-not-use";

    private static BrokerSecrets Secrets() => BrokerSecrets.FromEnvironment(name => name switch
    {
        BrokerSecrets.OrgKeyEnvVar => OrgKey,
        BrokerSecrets.SigningSecretEnvVar => "this-is-a-32-byte-signing-secret!!",
        _ => null,
    });

    [Fact]
    public async Task Forward_adds_the_org_key_server_side()
    {
        var handler = new CapturingHandler(CapturingHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var client = new HttpClient(handler);
        var forwarder = new AnthropicForwarder(client, Secrets(),
            Options.Create(new BrokerServiceOptions { ProviderBaseUrl = "https://upstream.example" }));

        using var _ = await forwarder.ForwardAsync("/v1/messages",
            Encoding.UTF8.GetBytes("{\"model\":\"claude\"}"), "application/json", CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://upstream.example/v1/messages");
        handler.LastRequest.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be(OrgKey);
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().ContainSingle();
    }

    [Fact]
    public async Task Forward_does_not_pass_a_client_authorization_header_upstream()
    {
        var handler = new CapturingHandler(CapturingHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var client = new HttpClient(handler);
        var forwarder = new AnthropicForwarder(client, Secrets(),
            Options.Create(new BrokerServiceOptions { ProviderBaseUrl = "https://upstream.example" }));

        using var _ = await forwarder.ForwardAsync("/v1/messages",
            Encoding.UTF8.GetBytes("{}"), "application/json", CancellationToken.None);

        // The forwarder only ever sets x-api-key; it never carries a client-sent bearer upstream.
        handler.LastRequest!.Headers.Contains("Authorization").Should().BeFalse();
    }

    [Fact]
    public async Task What_says_how_to_read_the_body_reaches_the_provider_with_the_body()
    {
        // A real turn through a running broker: the agent CLI posts to
        // /v1/messages?beta=true with anthropic-beta naming the features its body uses. The forwarder dropped
        // both, and the provider answered 400 "context_management: Extra inputs are not permitted".
        var handler = new CapturingHandler(CapturingHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var forwarder = new AnthropicForwarder(new HttpClient(handler), Secrets(),
            Options.Create(new BrokerServiceOptions { ProviderBaseUrl = "https://upstream.example" }));

        using var _ = await forwarder.ForwardAsync(
            new ForwardedCall(
                "/v1/messages", "?beta=true", Encoding.UTF8.GetBytes("{}"), "application/json",
                ForwardedCall.PassedThrough(
                [
                    ("anthropic-beta", "context-management-2025-06-27,claude-code-20250219"),
                    ("Anthropic-Version", "2024-10-22"),
                ])),
            CancellationToken.None);

        handler.LastRequest!.RequestUri!.ToString().Should().Be("https://upstream.example/v1/messages?beta=true");
        handler.LastRequest.Headers.GetValues("anthropic-beta").Should().ContainSingle()
            .Which.Should().Be("context-management-2025-06-27,claude-code-20250219");
        handler.LastRequest.Headers.GetValues("anthropic-version").Should().ContainSingle()
            .Which.Should().Be("2024-10-22", "the version belongs to whoever wrote the body, and is sent once");
    }

    [Fact]
    public async Task A_client_cannot_bring_its_own_key_or_any_header_nobody_named()
    {
        var handler = new CapturingHandler(CapturingHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var forwarder = new AnthropicForwarder(new HttpClient(handler), Secrets(),
            Options.Create(new BrokerServiceOptions { ProviderBaseUrl = "https://upstream.example" }));

        using var _ = await forwarder.ForwardAsync(
            new ForwardedCall(
                "/v1/messages", null, Encoding.UTF8.GetBytes("{}"), "application/json",
                ForwardedCall.PassedThrough(
                [
                    ("x-api-key", "sk-ant-the-clients-own"),
                    ("X-Api-Key", "sk-ant-in-another-case"),
                    ("Authorization", "Bearer the-broker-token"),
                    ("Cookie", "session=1"),
                    ("x-forwarded-for", "10.0.0.1"),
                    ("anthropic-beta", "   "),
                ])),
            CancellationToken.None);

        handler.LastRequest!.Headers.GetValues("x-api-key").Should().ContainSingle().Which.Should().Be(OrgKey);
        handler.LastRequest.Headers.Contains("Authorization").Should().BeFalse();
        handler.LastRequest.Headers.Contains("Cookie").Should().BeFalse();
        handler.LastRequest.Headers.Contains("x-forwarded-for").Should().BeFalse();
        handler.LastRequest.Headers.Contains("anthropic-beta").Should().BeFalse("a blank value is not a value");
    }

    [Fact]
    public async Task With_nothing_from_the_client_the_configured_version_is_sent()
    {
        var handler = new CapturingHandler(CapturingHandler.Json(HttpStatusCode.OK, "{\"ok\":true}"));
        var forwarder = new AnthropicForwarder(new HttpClient(handler), Secrets(),
            Options.Create(new BrokerServiceOptions { ProviderBaseUrl = "https://upstream.example", AnthropicVersion = "2023-06-01" }));

        using var _ = await forwarder.ForwardAsync("/v1/messages", Encoding.UTF8.GetBytes("{}"), null, CancellationToken.None);

        handler.LastRequest!.Headers.GetValues("anthropic-version").Should().ContainSingle().Which.Should().Be("2023-06-01");
    }

    [Fact]
    public async Task Response_returned_to_the_client_never_carries_the_org_key()
    {
        var handler = new CapturingHandler(CapturingHandler.Json(HttpStatusCode.OK, "{\"content\":\"pong\"}"));
        var client = new HttpClient(handler);
        var forwarder = new AnthropicForwarder(client, Secrets(),
            Options.Create(new BrokerServiceOptions { ProviderBaseUrl = "https://upstream.example" }));

        using var response = await forwarder.ForwardAsync("/v1/messages",
            Encoding.UTF8.GetBytes("{}"), "application/json", CancellationToken.None);
        var body = await response.Content.ReadAsStringAsync();

        body.Should().NotContain(OrgKey);
        response.Headers.Contains("x-api-key").Should().BeFalse();
        response.Content.Headers.Contains("x-api-key").Should().BeFalse();
    }
}
