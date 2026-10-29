using Fravashi.Broker.Tokens;

namespace Fravashi.Broker.Revocation;

/// <summary>
/// Tracks revoked tokens. Supports revoking a single token by its <c>jti</c> and revoking all of a
/// user's outstanding tokens by an as-of cutoff (any token issued at or before the cutoff for that user is
/// revoked - so "revoke user" works without a registry of every issued jti).
/// <para>
/// <b>Memory-only, restart-clears caveat (recorded, not hidden):</b> the default store lives in process memory,
/// consistent with keeping nothing on disk. A broker restart clears the revocation set; the short
/// token TTL bounds the exposure (a token a restart "un-revokes" still expires within its ≤ ~1h life). A
/// customer needing revocation to survive restarts supplies a broker-local store behind this interface that is
/// NOT the org key.
/// </para>
/// </summary>
public interface IRevocationStore
{
    /// <summary>Revoke a single token by its id (<c>jti</c>).</summary>
    void RevokeToken(string tokenId);

    /// <summary>Revoke every token issued at or before <paramref name="asOf"/> for the given user.</summary>
    void RevokeUser(string userId, DateTimeOffset asOf);

    /// <summary>True when the token has been revoked by jti or by its user's revoke-all cutoff.</summary>
    bool IsRevoked(BrokerToken token);
}
