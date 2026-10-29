using Fravashi.Broker.Identity;
using Fravashi.Broker.PromptAudit;

namespace Fravashi.Broker.Configuration;

/// <summary>
/// Non-secret, deploy-time broker configuration: the upstream provider base URL, the minted-token TTL, and the
/// per-user rate-limit + quota. Bound from configuration (env / appsettings) at startup and validated for
/// coherence by <see cref="Integrity.BrokerIntegrityCheck"/> - an incoherent config fails closed.
/// Secrets live in <see cref="BrokerSecrets"/>, never here.
/// </summary>
public sealed class BrokerServiceOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Broker";

    /// <summary>
    /// The upstream provider base URL the broker forwards to (Anthropic). The org key is added
    /// to requests to this host, server-side only. Must be an absolute https URL.
    /// </summary>
    public string ProviderBaseUrl { get; set; } = "https://api.anthropic.com";

    /// <summary>The Anthropic API version header sent upstream.</summary>
    public string AnthropicVersion { get; set; } = "2023-06-01";

    /// <summary>
    /// Escape hatch (default <c>false</c>): permit a non-https <see cref="ProviderBaseUrl"/>. Off by default so
    /// production requires https to the provider. Enable ONLY for an in-perimeter deployment that
    /// terminates TLS at a local sidecar/mesh (plaintext to a loopback/mesh address) or for a test upstream -
    /// never for a public provider endpoint. Surfaced as an owner-facing knob, not silently on.
    /// </summary>
    public bool AllowInsecureProviderUrl { get; set; }

    /// <summary>Minted-token lifetime. Default ≈ 1 hour, configurable per deployment.</summary>
    public TimeSpan TokenTtl { get; set; } = TimeSpan.FromHours(1);

    /// <summary>The scope minted tokens carry and the forward endpoint requires.</summary>
    public string TokenScope { get; set; } = "messages";

    /// <summary>Per-user rate limit: max forward requests allowed within <see cref="RateLimitWindow"/>.</summary>
    public int RateLimitRequests { get; set; } = 60;

    /// <summary>The rate-limit window.</summary>
    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Per-user quota: max forward requests allowed within <see cref="QuotaWindow"/> (a request budget).</summary>
    public int QuotaRequests { get; set; } = 1000;

    /// <summary>The quota window.</summary>
    public TimeSpan QuotaWindow { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// Per-user budget in the provider's own TOKEN figures within <see cref="QuotaWindow"/>: input,
    /// cache writes, cache reads and output together. Zero means no token limit, which is what a deployment from
    /// before this setting keeps until somebody sets one. The request quota above counts requests, and one
    /// request on the dearest model with a very large context costs what a thousand small ones do.
    /// </summary>
    public long QuotaTokens { get; set; }

    /// <summary>
    /// A ceiling on what EVERY user together may use in a day, in tokens. Zero means none. Ten users each inside
    /// their own budget are ten times the bill, and one leaked grant is a full one until somebody notices.
    /// </summary>
    public long OrganisationTokensPerDay { get; set; }

    /// <summary>
    /// What an answer is charged when its usage could not be read. Never nothing: a budget that can be slipped
    /// by an unreadable answer is no budget. The default is large on purpose.
    /// </summary>
    public long UnreadableUsageCharge { get; set; } = 200_000;

    /// <summary>
    /// How many refusals inside <see cref="SuspensionWindow"/> suspend a user. Zero means never,
    /// which is what a deployment from before this setting keeps. A suspension does not lift itself; an
    /// operator lifts it.
    /// </summary>
    public int SuspendAfterRefusals { get; set; }

    /// <summary>How fast refusals must arrive to count together. Not how long a suspension lasts.</summary>
    public TimeSpan SuspensionWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// The models this deployment's grant covers: exact ids, or a prefix ending in <c>*</c>. Empty means any
    /// model the provider offers, which the start-up log says out loud; once one is named, a request for any
    /// other model, or one whose model cannot be read, is refused.
    /// </summary>
    public List<string> AllowedModels { get; set; } = [];

    /// <summary>
    /// The stub/config identity validator's accepted proof (dev/test). When set, the mint endpoint accepts an
    /// identity proof equal to this value; when empty, the stub accepts any non-empty user id (dev default). Used
    /// only when <see cref="Identity"/> lists no providers - a real deployment configures <see cref="Identity"/>
    /// and the <see cref="SsoIdentityValidator"/> takes over.
    /// </summary>
    public string? StubIdentityProof { get; set; }

    /// <summary>
    /// The identity provider(s) this broker trusts to verify the identity proof presented at mint.
    /// When it lists at least one provider the <see cref="SsoIdentityValidator"/> is used (real IdP verification);
    /// when empty the <see cref="ConfigIdentityValidator"/> dev/test stub is used. Bound from <c>Broker:Identity</c>.
    /// </summary>
    public BrokerIdentityOptions Identity { get; set; } = new();

    /// <summary>
    /// Inspection of the user prompts every request carries. On exactly when its key is configured;
    /// bound from <c>Broker:PromptAudit</c>.
    /// </summary>
    public PromptAuditOptions PromptAudit { get; set; } = new();
}
