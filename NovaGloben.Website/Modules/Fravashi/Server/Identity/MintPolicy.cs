namespace Fravashi.Broker.Identity;

/// <summary>
/// Who, among the users the IdP authenticates, may actually mint (DENY BY DEFAULT).
/// Authentication answers "who are you"; this answers "may you spend the organization's LLM budget", and the two
/// are deliberately separate: an IdP that allows self-registration would otherwise hand a stranger a funded token.
/// Pure and side-effect-free, so every arm is seen to fire in tests.
/// </summary>
public static class MintPolicy
{
    /// <summary>The audit reason recorded when an authenticated identity is refused by the allowlist.</summary>
    public const string DeniedReason = "not_allowlisted";

    /// <summary>
    /// True when <paramref name="identity"/> appears in <paramref name="allowlist"/>. An entry matches the
    /// canonical id, the bare subject after the provider prefix, the email, or the username, case-insensitively,
    /// so operators can write <c>ada@example.com</c> instead of <c>oidc:2b1c-...</c>. An EMPTY allowlist
    /// allows nobody (the startup check refuses to serve in that state; this is the second line of the same rule).
    /// </summary>
    public static bool IsAllowed(ValidatedIdentity identity, IReadOnlyCollection<string> allowlist)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(allowlist);

        if (allowlist.Count == 0)
        {
            return false;
        }

        var subject = identity.CanonicalId;
        var colon = subject.IndexOf(':');
        var bare = colon >= 0 && colon < subject.Length - 1 ? subject[(colon + 1)..] : subject;

        foreach (var raw in allowlist)
        {
            var entry = raw?.Trim();
            if (string.IsNullOrEmpty(entry))
            {
                continue;
            }

            if (Matches(entry, identity.CanonicalId)
                || Matches(entry, bare)
                || Matches(entry, identity.Email)
                || Matches(entry, identity.Username))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The audit reason recorded when somebody who is not an operator asks an operator's question.</summary>
    public const string NotAnOperatorReason = "not_an_operator";

    /// <summary>
    /// True when this identity may read what the broker has been doing and lift a suspension. Named or
    /// role-holding, exactly as minting is; configured with neither, nobody is an operator.
    /// </summary>
    public static bool IsOperator(ValidatedIdentity identity, BrokerIdentityOptions identityOptions)
    {
        ArgumentNullException.ThrowIfNull(identityOptions);
        return IsAllowed(identity, identityOptions.Operators) || HoldsRole(identity, identityOptions.OperatorRole);
    }

    /// <summary>The audit reason recorded when a required role is configured and the identity is refused.</summary>
    public const string NotEntitledReason = "not_entitled";

    /// <summary>
    /// True when the IdP said <paramref name="identity"/> holds <paramref name="requiredRole"/>. A blank
    /// required role entitles nobody, which is the same shape as the empty allowlist: absence of configuration
    /// is never a permission. The match is whole and case-insensitive (IdPs normalise role names); a role is
    /// never a prefix or a pattern.
    /// </summary>
    public static bool HoldsRole(ValidatedIdentity identity, string? requiredRole)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var wanted = requiredRole?.Trim();
        if (string.IsNullOrEmpty(wanted))
        {
            return false;
        }

        foreach (var role in identity.Roles)
        {
            if (Matches(wanted, role))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The whole entitlement decision: null when <paramref name="identity"/> may mint, otherwise the reason to
    /// audit and answer with. Entitled means listed OR holding the required role. The reason says which rule
    /// an operator has to act on: with a required role configured it is <see cref="NotEntitledReason"/> (grant
    /// the role in the IdP), without one it stays <see cref="DeniedReason"/> (add the user to the allowlist).
    /// </summary>
    public static string? Refusal(ValidatedIdentity identity, BrokerIdentityOptions options)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(options);

        if (IsAllowed(identity, options.Allowlist) || HoldsRole(identity, options.RequiredRole))
        {
            return null;
        }

        return string.IsNullOrWhiteSpace(options.RequiredRole) ? DeniedReason : NotEntitledReason;
    }

    private static bool Matches(string entry, string? candidate) =>
        !string.IsNullOrWhiteSpace(candidate)
        && string.Equals(entry, candidate.Trim(), StringComparison.OrdinalIgnoreCase);
}
