using System.Text;

namespace Fravashi.Broker.Configuration;

/// <summary>
/// The broker's two secrets, injected at deploy time and held <b>in memory only</b>: the
/// organization's real LLM key (added to upstream requests server-side, never returned to a client) and the
/// HMAC token-signing secret (mints and validates per-user tokens). Neither is ever written to disk, logged, or
/// echoed. A broker missing either <b>refuses to start</b>: <see cref="FromEnvironment"/> throws
/// <see cref="BrokerConfigurationException"/>, which the host turns into a fail-closed exit.
/// </summary>
public sealed class BrokerSecrets
{
    /// <summary>Environment variable carrying the organization's real provider (Anthropic) key. Deploy-time only.</summary>
    public const string OrgKeyEnvVar = "BROKER_ORG_API_KEY";

    /// <summary>Environment variable carrying the HMAC token-signing secret. Deploy-time only.</summary>
    public const string SigningSecretEnvVar = "BROKER_TOKEN_SIGNING_SECRET";

    /// <summary>
    /// Environment variable carrying the prompt-inspection service's key. OPTIONAL: without it the
    /// broker forwards uninspected and says so at start-up, unless <c>Broker:PromptAudit:Required</c> is set.
    /// </summary>
    public const string PromptAuditKeyEnvVar = "BROKER_PROMPT_AUDIT_KEY";

    /// <summary>
    /// The <c>_FILE</c> convention every container secret store speaks (Docker/Compose/Swarm secrets, Kubernetes
    /// projected volumes): the variable names a FILE whose content is the secret, so the value never appears in
    /// the process environment, an image layer, or a `docker inspect`. The direct variable still wins when both
    /// are set; a named-but-unreadable file is a fail-closed error rather than a silent fallback.
    /// </summary>
    public const string FileSuffix = "_FILE";

    /// <summary>
    /// Minimum signing-secret length. A short secret weakens the HMAC; an incoherent (too-weak) security config
    /// is a fail-closed condition, not a warning.
    /// </summary>
    public const int MinSigningSecretLength = 32;

    private BrokerSecrets(string orgKey, byte[] signingKey, string? promptAuditKey)
    {
        OrgApiKey = orgKey;
        SigningKey = signingKey;
        PromptAuditKey = promptAuditKey;
    }

    /// <summary>The organization's provider key. Added to upstream requests server-side only; never serialized out.</summary>
    public string OrgApiKey { get; }

    /// <summary>The HMAC signing key bytes (UTF-8 of the configured secret). Never serialized out.</summary>
    public byte[] SigningKey { get; }

    /// <summary>
    /// The prompt-inspection service's key, or null when none is configured. Sent to that service
    /// only, by its adapter; never serialized out.
    /// </summary>
    public string? PromptAuditKey { get; }

    /// <summary>
    /// Reads both secrets from the environment, holding them in memory. <b>Fails closed</b>: throws
    /// <see cref="BrokerConfigurationException"/> if the org key is absent, or the signing secret is absent or
    /// shorter than <see cref="MinSigningSecretLength"/>. The exception message names the problem, never a value.
    /// </summary>
    public static BrokerSecrets FromEnvironment(Func<string, string?> getEnv)
        => FromEnvironment(getEnv, File.ReadAllText);

    /// <summary>
    /// The same load with the file reader injected, so the <c>_FILE</c> paths are seen to fire in tests without
    /// touching the real filesystem.
    /// </summary>
    public static BrokerSecrets FromEnvironment(Func<string, string?> getEnv, Func<string, string> readFile)
    {
        ArgumentNullException.ThrowIfNull(getEnv);
        ArgumentNullException.ThrowIfNull(readFile);

        var orgKey = Resolve(OrgKeyEnvVar, getEnv, readFile);
        if (string.IsNullOrWhiteSpace(orgKey))
        {
            throw new BrokerConfigurationException(
                $"The organization LLM key is absent (expected in ${OrgKeyEnvVar} or ${OrgKeyEnvVar}{FileSuffix}). " +
                "A broker with no org key must not run: inject it at deploy time (env or secret mount).");
        }

        var signingSecret = Resolve(SigningSecretEnvVar, getEnv, readFile);
        if (string.IsNullOrWhiteSpace(signingSecret))
        {
            throw new BrokerConfigurationException(
                $"The token-signing secret is absent (expected in ${SigningSecretEnvVar} or " +
                $"${SigningSecretEnvVar}{FileSuffix}). A broker that cannot sign or validate tokens must not run: " +
                "inject it at deploy time.");
        }

        if (signingSecret.Length < MinSigningSecretLength)
        {
            throw new BrokerConfigurationException(
                $"The token-signing secret is too weak ({signingSecret.Length} chars; minimum " +
                $"{MinSigningSecretLength}). An incoherent security config fails closed.");
        }

        // Optional, but under the same _FILE rule: a file that is NAMED and unreadable still fails closed, because
        // a deploy that meant to switch inspection on and did not must not quietly run without it.
        var promptAuditKey = Resolve(PromptAuditKeyEnvVar, getEnv, readFile);

        return new BrokerSecrets(
            orgKey,
            Encoding.UTF8.GetBytes(signingSecret),
            string.IsNullOrWhiteSpace(promptAuditKey) ? null : promptAuditKey);
    }

    /// <summary>
    /// The direct variable, else the <c>_FILE</c> variable's contents. A named file that cannot be read fails
    /// closed with a message naming the VARIABLE (never the path's contents), because a deploy that meant to
    /// mount a secret and did not must not fall back to "no secret".
    /// </summary>
    private static string? Resolve(string variable, Func<string, string?> getEnv, Func<string, string> readFile)
    {
        if (getEnv(variable)?.Trim() is { Length: > 0 } direct)
        {
            return direct;
        }

        var path = getEnv(variable + FileSuffix)?.Trim();
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return readFile(path).Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new BrokerConfigurationException(
                $"${variable}{FileSuffix} names a secret file the broker cannot read ({ex.GetType().Name}). " +
                "A missing mount must fail closed, not fall back to an unauthenticated broker.");
        }
    }
}
