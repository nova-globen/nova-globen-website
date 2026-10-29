namespace Fravashi.Broker.Identity;

/// <summary>
/// Who the IdP vouched for. <see cref="CanonicalId"/> is what a token is attributed to (stable, provider-scoped,
/// e.g. <c>oidc:{sub}</c>); <see cref="Email"/> and <see cref="Username"/> are the human-readable claims the IdP
/// also returned, carried so the mint allowlist can be written in terms an operator can actually administer
/// (an email, not a GUID). They are identity ATTRIBUTES, never authorization by themselves.
/// </summary>
public sealed record ValidatedIdentity(string CanonicalId, string? Email = null, string? Username = null)
{
    /// <summary>
    /// The roles the IdP userinfo answer carried for this user (the <c>role</c> or <c>roles</c> claim of an
    /// <c>oidc</c> provider), empty when it carried none. They are a statement by the IdP, read from the same
    /// answer as the subject, and they are what <see cref="MintPolicy"/> tests a required role against.
    /// </summary>
    public IReadOnlyList<string> Roles { get; init; } = [];
}

/// <summary>
/// Validates the user identity presented to the mint endpoint before a token is issued. This is the
/// <b>identity-proof contract</b>: a config/stub validator and the real validator both implement it; the real one
/// wires the real SSO/OAuth IdP verification behind the same interface, so the mint endpoint and the
/// forward/quota/audit machinery do not change when the real verifier lands.
/// </summary>
public interface IIdentityValidator
{
    /// <summary>
    /// Validates an identity proof for a claimed user id. Returns the identity to attribute the token to when the
    /// proof is accepted, or null when it is rejected. Implementations must not log the proof.
    /// </summary>
    ValueTask<ValidatedIdentity?> ValidateAsync(string userId, string? identityProof, CancellationToken cancellationToken);
}
