namespace Fravashi.Broker.Forwarding;

/// <summary>
/// What the broker sends upstream on a client's behalf: the path, the query, the body, and the FEW client
/// headers that say how the body is to be read. Authority never comes from here. The organization's key is
/// added by the forwarder, and nothing a client sends can name, replace or accompany it.
/// </summary>
/// <param name="Path">The provider path, e.g. <c>/v1/messages</c>.</param>
/// <param name="Query">The client's query string, with or without its leading question mark; null for none.</param>
/// <param name="Body">The client's body, byte for byte.</param>
/// <param name="ContentType">The client's content type; JSON when absent.</param>
/// <param name="ClientHeaders">Client headers already narrowed by <see cref="PassedThrough"/>.</param>
public sealed record ForwardedCall(
    string Path,
    string? Query,
    byte[] Body,
    string? ContentType,
    IReadOnlyList<(string Name, string Value)> ClientHeaders)
{
    /// <summary>No client headers at all.</summary>
    public static readonly IReadOnlyList<(string Name, string Value)> NoHeaders = [];

    /// <summary>
    /// The client headers that reach the provider, by exact name. Both say how the body is to be read and
    /// neither grants anything: <c>anthropic-beta</c> opts the body into features the agent CLI uses in every
    /// request, and <c>anthropic-version</c> is the API version the body was written for. Adding a name here
    /// is a decision about what a client may say to the provider under the organization's key, so it is made
    /// one name at a time. <c>Authorization</c>, <c>x-api-key</c>, cookies and everything else are not here.
    /// </summary>
    private static readonly string[] Allowed = ["anthropic-beta", "anthropic-version"];

    /// <summary>True when <paramref name="headerName"/> is one a client may send through.</summary>
    public static bool IsPassedThrough(string headerName) =>
        Allowed.Contains(headerName, StringComparer.OrdinalIgnoreCase);

    /// <summary>Narrows a request's headers to the allowed ones, dropping blanks.</summary>
    public static IReadOnlyList<(string Name, string Value)> PassedThrough(IEnumerable<(string Name, string Value)> headers)
    {
        ArgumentNullException.ThrowIfNull(headers);
        return [.. headers.Where(h => IsPassedThrough(h.Name) && !string.IsNullOrWhiteSpace(h.Value))];
    }
}
