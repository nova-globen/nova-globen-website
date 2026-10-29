using Fravashi.Broker.Configuration;
using Fravashi.Broker.Identity;
using Fravashi.Broker.PromptAudit;

namespace Fravashi.Broker.Integrity;

/// <summary>The result of the broker's startup integrity self-check: coherent or not, with secret-free problems.</summary>
/// <param name="Ok">True when the security config is present and coherent.</param>
/// <param name="Problems">Human-readable, secret-free descriptions of each incoherence (empty when Ok).</param>
public sealed record BrokerIntegrityResult(bool Ok, IReadOnlyList<string> Problems);

/// <summary>
/// The broker's fail-closed integrity self-check, applied to its <b>security configuration</b>
///. It asserts the required secrets are present and coherent and the non-secret config is sane
/// (absolute https provider URL, positive TTL, positive rate/quota). <see cref="BrokerSecrets.FromEnvironment"/>
/// already fails closed on absent/weak secrets; this catches the remaining incoherent-config cases. The host
/// calls <see cref="Evaluate"/> at startup and <b>refuses to serve</b> (non-zero exit) on any problem - a bad
/// security config never serves a request. Pure and side-effect-free, so it is seen-to-fire in tests.
/// </summary>
public static class BrokerIntegrityCheck
{
    public static BrokerIntegrityResult Evaluate(BrokerSecrets secrets, BrokerServiceOptions options)
    {
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(secrets.OrgApiKey))
        {
            problems.Add("The organization key is empty - the broker cannot forward without it.");
        }

        if (secrets.SigningKey.Length < BrokerSecrets.MinSigningSecretLength)
        {
            problems.Add(
                $"The signing key is too short ({secrets.SigningKey.Length} bytes; minimum " +
                $"{BrokerSecrets.MinSigningSecretLength}) - token signatures would be weak.");
        }

        if (!Uri.TryCreate(options.ProviderBaseUrl, UriKind.Absolute, out var providerUri))
        {
            problems.Add("The provider base URL must be an absolute URL.");
        }
        else if (providerUri.Scheme != Uri.UriSchemeHttps &&
                 !(options.AllowInsecureProviderUrl && providerUri.Scheme == Uri.UriSchemeHttp))
        {
            problems.Add("The provider base URL must be an absolute https URL (or set AllowInsecureProviderUrl for an in-perimeter/test http upstream).");
        }

        if (options.TokenTtl <= TimeSpan.Zero)
        {
            problems.Add("The token TTL must be positive.");
        }

        if (options.RateLimitRequests <= 0 || options.RateLimitWindow <= TimeSpan.Zero)
        {
            problems.Add("The rate limit and its window must be positive.");
        }

        if (options.QuotaRequests <= 0 || options.QuotaWindow <= TimeSpan.Zero)
        {
            problems.Add("The quota and its window must be positive.");
        }

        if (string.IsNullOrWhiteSpace(options.TokenScope))
        {
            problems.Add("The token scope must be set.");
        }

        // Minting is deny-by-default. A broker wired to a real IdP but with no
        // allowlist would authenticate strangers into the organization's LLM budget the moment the IdP allows
        // self-registration, so an absent allowlist is an incoherent security config, not a permissive default.
        // Entitlement has two arms, the allowlist and a role the IdP vouches for (Broker:Identity:RequiredRole).
        // Either one configured is a coherent deny-by-default posture; neither is not.
        var hasRole = !string.IsNullOrWhiteSpace(options.Identity.RequiredRole);
        if (options.Identity.Providers.Count > 0 && options.Identity.Allowlist.Count == 0 && !hasRole)
        {
            problems.Add(
                "No mint entitlement is configured while a real identity provider is trusted: neither an " +
                "allowlist (Broker:Identity:Allowlist) nor a required role (Broker:Identity:RequiredRole). " +
                "Minting is deny-by-default: list the users (email, username, subject or canonical id) who may " +
                "mint, or name the role the identity provider grants them, or the broker would authenticate " +
                "users it must not fund.");
        }

        // A required role can only ever be held through an oidc provider (the one kind whose userinfo answer is
        // read for roles). With no such provider and no allowlist, nobody could ever mint, and a broker that
        // starts cleanly and refuses everyone is a misconfiguration that looks like an outage.
        if (hasRole
            && options.Identity.Allowlist.Count == 0
            && options.Identity.Providers.Count > 0
            && !options.Identity.Providers.Any(p =>
                string.Equals(p.Kind, TrustedIdentityProvider.Oidc, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add(
                "A required role is configured (Broker:Identity:RequiredRole) but no oidc identity provider is " +
                "trusted, and only an oidc provider is asked for roles. Nobody could mint.");
        }

        problems.AddRange(options.TokenWeights.Problems());
        CheckPromptAudit(secrets, options.PromptAudit, problems);

        return new BrokerIntegrityResult(problems.Count == 0, problems);
    }

    /// <summary>
    /// Prompt inspection. With no key it is off and that is coherent (the start-up log says so),
    /// unless the deployment said it is required. With a key, everything it would decide with must make sense,
    /// because a threshold above 1 would refuse nothing and one at 0 would refuse everything, and either would
    /// start cleanly and look like it worked.
    /// </summary>
    private static void CheckPromptAudit(BrokerSecrets secrets, PromptAuditOptions audit, List<string> problems)
    {
        var hasKey = !string.IsNullOrWhiteSpace(secrets.PromptAuditKey);
        if (audit.Required && !hasKey)
        {
            problems.Add(
                $"Prompt inspection is required (Broker:PromptAudit:Required) but no key is configured " +
                $"(${BrokerSecrets.PromptAuditKeyEnvVar} or ${BrokerSecrets.PromptAuditKeyEnvVar}{BrokerSecrets.FileSuffix}).");
        }

        if (!hasKey)
        {
            return;
        }

        if (!string.Equals(audit.Provider, PromptAuditOptions.TypeSafe, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"Broker:PromptAudit:Provider names '{audit.Provider}', which this broker has no adapter for.");
        }

        if (!Uri.TryCreate(audit.Endpoint, UriKind.Absolute, out var endpoint)
            || !(endpoint.Scheme == Uri.UriSchemeHttps || (audit.AllowInsecureEndpoint && endpoint.Scheme == Uri.UriSchemeHttp)))
        {
            problems.Add("Broker:PromptAudit:Endpoint must be an absolute https URL: every prompt is sent to it.");
        }

        if (string.IsNullOrWhiteSpace(audit.Model))
        {
            problems.Add("Broker:PromptAudit:Model must name a model.");
        }

        if (!(audit.CautionAt > 0 && audit.CautionAt <= audit.RefuseAt && audit.RefuseAt <= 1))
        {
            problems.Add("Broker:PromptAudit thresholds must satisfy 0 < CautionAt <= RefuseAt <= 1.");
        }

        if (audit.FlagAt is <= 0 or > 1 || audit.SensitiveIntentWeight is < 0 or > 1
            || audit.CategoryWeights.Values.Any(w => w is < 0 or > 1) || audit.DefaultCategoryWeight is < 0 or > 1)
        {
            problems.Add("Broker:PromptAudit weights and FlagAt must lie between 0 and 1.");
        }

        if (audit.Timeout <= TimeSpan.Zero || audit.RetryDelay < TimeSpan.Zero || audit.RetryDelay >= audit.Timeout)
        {
            problems.Add("Broker:PromptAudit:Timeout must be positive and longer than RetryDelay.");
        }

        if (audit.MaxChunkChars < 1000 || audit.MaxCallsPerRequest < 1 || audit.MaxParallelCalls < 1
            || audit.VerdictCacheEntries < 1 || audit.VerdictTtl < TimeSpan.Zero)
        {
            problems.Add(
                "Broker:PromptAudit limits are incoherent: MaxChunkChars at least 1000, MaxCallsPerRequest, " +
                "MaxParallelCalls and VerdictCacheEntries at least 1, VerdictTtl not negative.");
        }
    }
}
