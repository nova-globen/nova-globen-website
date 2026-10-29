using System.Net.Http.Headers;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.Forwarding;

/// <summary>
/// <see cref="IUpstreamForwarder"/> for the Anthropic Messages API. It sets the
/// organization's key as the provider's <c>x-api-key</c> header and the <c>anthropic-version</c> header, and
/// sends the client's body upstream. The org key comes from the in-memory <see cref="BrokerSecrets"/> and is
/// added <b>only</b> here - no client-supplied auth header is forwarded, and the key never appears on the
/// response returned to the client.
/// </summary>
public sealed class AnthropicForwarder(
    HttpClient httpClient,
    BrokerSecrets secrets,
    IOptions<BrokerServiceOptions> options) : IUpstreamForwarder
{
    private readonly BrokerServiceOptions _options = options.Value;

    /// <summary>A call with nothing from the client but its body: no query and no headers.</summary>
    public Task<HttpResponseMessage> ForwardAsync(
        string path, byte[] body, string? contentType, CancellationToken cancellationToken) =>
        ForwardAsync(new ForwardedCall(path, null, body, contentType, ForwardedCall.NoHeaders), cancellationToken);

    /// <inheritdoc />
    public async Task<HttpResponseMessage> ForwardAsync(ForwardedCall call, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(call);

        // The query rides along: the agent CLI posts to /v1/messages?beta=true, and without it the provider
        // reads the same body under different rules. It cannot carry authority, the provider takes no key there.
        var target = call.Path.TrimStart('/') + (string.IsNullOrEmpty(call.Query) ? string.Empty : "?" + call.Query.TrimStart('?'));
        var uri = new Uri(new Uri(_options.ProviderBaseUrl.TrimEnd('/') + "/"), target);
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new ByteArrayContent(call.Body),
        };

        request.Content.Headers.ContentType =
            MediaTypeHeaderValue.Parse(string.IsNullOrWhiteSpace(call.ContentType) ? "application/json" : call.ContentType);

        // The org key is injected here, server-side only. Client-sent auth is never forwarded.
        request.Headers.TryAddWithoutValidation("x-api-key", secrets.OrgApiKey);

        // The headers that say HOW TO READ THE BODY come from the client that wrote the body. Met on a real
        // turn through a running broker: the agent CLI opts into beta features in every
        // request, this forwarder dropped `anthropic-beta`, and the provider answered 400 "context_management:
        // Extra inputs are not permitted". It is an ALLOWLIST by name and stays one: copying "everything but
        // auth" is how a client header nobody thought of ends up meaning something upstream.
        var version = _options.AnthropicVersion;
        foreach (var (name, value) in call.ClientHeaders)
        {
            if (string.IsNullOrWhiteSpace(value) || !ForwardedCall.IsPassedThrough(name))
            {
                continue;
            }

            if (string.Equals(name, "anthropic-version", StringComparison.OrdinalIgnoreCase))
            {
                version = value;
            }
            else
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        request.Headers.TryAddWithoutValidation("anthropic-version", version);
        request.Headers.Accept.ParseAdd("application/json");

        return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);
    }
}
