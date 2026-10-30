namespace Fravashi.Broker.Forwarding;

/// <summary>
/// What the client is told when the provider could not be reached or did not answer in time: 502
/// <c>upstream_unreachable</c> or 504 <c>upstream_timeout</c>, as JSON, instead of an unhandled failure. A client that
/// hung up is not a provider failure and gets nothing (null). No units are charged, since the provider never
/// answered; the request was already counted against the person's request quota before the call.
/// </summary>
public static class ForwardFailure
{
    public const string Unreachable = "upstream_unreachable";
    public const string Timeout = "upstream_timeout";

    /// <summary>The status and reason for a failed call, or null when it is not one this answers.</summary>
    public static (int Status, string Reason)? For(Exception exception, bool clientGone) => exception switch
    {
        HttpRequestException => (502, Unreachable),
        TaskCanceledException when !clientGone => (504, Timeout),
        _ => null,
    };
}