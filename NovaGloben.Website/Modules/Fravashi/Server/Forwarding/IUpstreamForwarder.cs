namespace Fravashi.Broker.Forwarding;

/// <summary>
/// Forwards an authorized request to the upstream provider (Anthropic in v1) with the organization's key added
/// <b>server-side only</b>. The org key is set here and nowhere else on the response path, so it is
/// never returned to, or visible to, the client. Supports streaming/SSE passthrough - the returned response is
/// read headers-first and its body streamed back to the caller.
/// </summary>
public interface IUpstreamForwarder
{
    /// <summary>
    /// Forwards a POST with <paramref name="body"/> to <c>{ProviderBaseUrl}{path}</c>, injecting the org key and
    /// the provider version header. Returns the upstream response (headers read, body not yet buffered) for the
    /// endpoint to stream back.
    /// </summary>
    Task<HttpResponseMessage> ForwardAsync(ForwardedCall call, CancellationToken cancellationToken);
}
