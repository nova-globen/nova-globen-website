namespace Fravashi.Broker.Identity;

/// <summary>
/// Which identity provider(s) this broker trusts to vouch for a user, and WHICH of
/// those users may mint (<see cref="Allowlist"/>). A customer hosts the broker inside
/// their own perimeter and configures the IdP <b>they</b> run: the mint endpoint accepts an identity proof (the
/// user's SSO access token) only if one of these providers verifies it AND the resulting identity is allowlisted.
/// Bound from the <c>Broker:Identity</c> configuration section. When <see cref="Providers"/> is empty the broker
/// falls back to the <see cref="ConfigIdentityValidator"/> dev/test stub: a real deployment always lists at least
/// one provider, and then an allowlist is mandatory (the startup integrity check refuses to serve without one).
/// </summary>
public sealed class BrokerIdentityOptions
{
    /// <summary>The trusted identity providers, tried in order until one accepts the proof.</summary>
    public List<TrustedIdentityProvider> Providers { get; set; } = [];

    /// <summary>
    /// Who may mint, DENY BY DEFAULT: an authenticated user who is not listed here is
    /// refused, because authenticating at the IdP is not the same as being entitled to spend the organization's
    /// LLM budget (that matters most where the IdP allows self-registration). Each entry matches, case-insensitively,
    /// any of: the canonical id (<c>oidc:{sub}</c>), the bare subject, the email, or the username the IdP returned,
    /// so an operator can write real emails instead of GUIDs. Empty + a configured provider is a fail-closed
    /// configuration error, never an implicit allow-all.
    /// </summary>
    public List<string> Allowlist { get; set; } = [];

    /// <summary>
    /// A role that ENTITLES its holder to mint, so entitlement can be managed where users already are (the
    /// admin UI of the IdP) and the broker stays stateless. A user is entitled when they are on the
    /// <see cref="Allowlist"/> OR the IdP userinfo answer says they hold this role (the <c>role</c> or
    /// <c>roles</c> claim of an <c>oidc</c> provider, matched case-insensitively and whole). Blank entitles
    /// nobody. It is still deny-by-default: a user whose answer carries no roles at all, because the IdP does
    /// not emit them or the studio did not request the scope that makes it, is refused like any stranger.
    /// </summary>
    public string? RequiredRole { get; set; }

    /// <summary>
    /// Who may READ what this broker has been doing and lift a suspension. Matched exactly as
    /// <see cref="Allowlist"/> is, so an operator is named the same way a user is. Empty entitles nobody,
    /// which is the same rule as everywhere else here: absence of configuration is never a permission, and an
    /// operator endpoint nobody can reach is better than one anybody can.
    /// </summary>
    public List<string> Operators { get; set; } = [];

    /// <summary>A role the IdP vouches for that makes somebody an operator, beside <see cref="Operators"/>.</summary>
    public string? OperatorRole { get; set; }
}

/// <summary>
/// One trusted IdP the broker verifies an identity proof against. <see cref="Kind"/> selects how the proof is
/// checked and how the canonical user id is shaped; <see cref="UserInfoUrl"/> overrides the default userinfo
/// endpoint (a self-hosted GitHub Enterprise, a sovereign Entra cloud, or a stub IdP under test).
/// </summary>
public sealed class TrustedIdentityProvider
{
    /// <summary>GitHub: verify by calling the user endpoint. Canonical id <c>github:{id}</c>.</summary>
    public const string GitHub = "github";

    /// <summary>Microsoft Entra: verify by calling the Graph <c>/me</c> endpoint. Canonical id <c>entra:{oid}</c>.</summary>
    public const string Entra = "entra";

    /// <summary>
    /// Any standards-compliant OpenID Connect provider (a Stomme site's own IdP, for one): verify by calling
    /// the issuer's userinfo endpoint with the access token. Canonical id <c>oidc:{sub}</c>. Verified against a
    /// real Stomme token: a genuine token answers 200 with <c>sub</c>/<c>email</c>, a forged one 401.
    /// </summary>
    public const string Oidc = "oidc";

    /// <summary>The provider kind: <see cref="GitHub"/>, <see cref="Entra"/>, or <see cref="Oidc"/>.</summary>
    public string Kind { get; set; } = string.Empty;

    /// <summary>
    /// The OIDC issuer (e.g. <c>https://idp.example.com</c>). For <see cref="Oidc"/> providers the userinfo
    /// endpoint is discovered from <c>{issuer}/.well-known/openid-configuration</c> when
    /// <see cref="UserInfoUrl"/> is not set; discovery is cached for the process lifetime.
    /// </summary>
    public string? Issuer { get; set; }

    /// <summary>
    /// Override for the userinfo endpoint the proof is checked against. Null uses the provider default
    /// (<c>https://api.github.com/user</c> / <c>https://graph.microsoft.com/v1.0/me</c>) or, for
    /// <see cref="Oidc"/>, the discovered endpoint. The presented token must be valid <b>for this endpoint</b>.
    /// </summary>
    public string? UserInfoUrl { get; set; }
}
