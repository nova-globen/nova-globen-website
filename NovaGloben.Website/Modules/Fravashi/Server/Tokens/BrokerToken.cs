namespace Fravashi.Broker.Tokens;

/// <summary>
/// The claim set carried by a broker-minted, HMAC-signed token. Short-lived, per-user, scoped, and
/// revocable: an extracted token expires (<see cref="ExpiresAt"/>), is attributable to one user
/// (<see cref="Subject"/>), and is revocable by its id (<see cref="TokenId"/>, the <c>jti</c>). No secret is
/// carried in the token - only these claims, signed with the in-memory signing key.
/// </summary>
/// <param name="Subject">The user id the token is minted for (attribution).</param>
/// <param name="Scope">The scope the token grants (e.g. <c>messages</c>).</param>
/// <param name="TokenId">The unique token id (<c>jti</c>) used for revocation.</param>
/// <param name="IssuedAt">When the token was minted.</param>
/// <param name="ExpiresAt">When the token expires.</param>
public sealed record BrokerToken(
    string Subject,
    string Scope,
    string TokenId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt);
