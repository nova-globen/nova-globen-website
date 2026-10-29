namespace Fravashi.Broker.PromptAudit;

/// <summary>What the broker does when there is no judgement to be had (the service is down, late, or unreadable).</summary>
public enum PromptAuditFallback
{
    /// <summary>Refuse the request with 503 and say why. The default: "fully inspected" means nothing unjudged passes.</summary>
    Refuse,

    /// <summary>Forward it anyway and record that it went unjudged. For a deployment that prefers service to inspection.</summary>
    Forward,
}

/// <summary>
/// Prompt inspection, bound from <c>Broker:PromptAudit</c>. It is ON exactly when a key is
/// configured (<c>BROKER_PROMPT_AUDIT_KEY</c> or its <c>_FILE</c>), and says so at start-up either way. Off is
/// allowed without a key for the same reason an empty model list is allowed (see <c>ModelPolicy</c>): a
/// broker already running without a key must not have an image that refuses to start until its configuration
/// catches up, which would take its conversations down on upgrade.
/// <see cref="Required"/> is how a deployment says "never without it" once the key is in place.
/// </summary>
public sealed class PromptAuditOptions
{
    /// <summary>The one provider known today. A new System One adapter adds its name here and a case in <c>Program</c>.</summary>
    public const string TypeSafe = "typesafe";

    /// <summary>Which System One adapter answers. Unknown names fail the start-up integrity check.</summary>
    public string Provider { get; set; } = TypeSafe;

    /// <summary>The adapter's endpoint. For TypeSafe, <c>POST /v1/systemone</c>. Must be https.</summary>
    public string Endpoint { get; set; } = "https://api.typesafe.ai/v1/systemone";

    /// <summary>Test escape hatch: permit an http <see cref="Endpoint"/> (a stub under test). Never for a real service.</summary>
    public bool AllowInsecureEndpoint { get; set; }

    /// <summary>The model the adapter asks for.</summary>
    public string Model { get; set; } = "jev-latest";

    /// <summary>When true, a broker with no key REFUSES TO START instead of forwarding uninspected.</summary>
    public bool Required { get; set; }

    /// <summary>How long one call may take, retries included. A System One call is typically a tenth of a second.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>The wait before the one retry a 429 or 529 earns.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(300);

    /// <summary>What happens when there is no judgement. See <see cref="PromptAuditFallback"/>.</summary>
    public PromptAuditFallback WhenUnavailable { get; set; } = PromptAuditFallback.Refuse;

    /// <summary>At or above this execution risk a request is forwarded but recorded as CAUTIONED.</summary>
    public double CautionAt { get; set; } = 0.4;

    /// <summary>At or above this execution risk a request is REFUSED.</summary>
    public double RefuseAt { get; set; } = 0.75;

    /// <summary>A category question whose yes-value reaches this is named on the audit line and in a refusal.</summary>
    public double FlagAt { get; set; } = 0.5;

    /// <summary>How much a "sensitive but legitimate" intent counts toward the intent risk, beside "harmful" at 1.</summary>
    public double SensitiveIntentWeight { get; set; } = 0.25;

    /// <summary>
    /// How much each category's yes-value counts, 0..1. A category's risk is its value times its weight, and the
    /// highest one stands on its own: at weight 1 a category alone can refuse, below <see cref="RefuseAt"/> it
    /// never can. A category missing here counts at <see cref="DefaultCategoryWeight"/>. The defaults put the
    /// harms with no legitimate reading (child safety, CBRN) at 1 and the ones a coding studio meets in honest
    /// work (security, privacy) lower, because a coding studio's own containment work reads like an attack.
    /// </summary>
    public Dictionary<string, double> CategoryWeights { get; set; } = new(StringComparer.Ordinal)
    {
        ["child_safety"] = 1.0,
        ["weapons_cbrn"] = 1.0,
        ["extremism_hate"] = 0.85,
        ["violence_or_self_harm"] = 0.85,
        ["military_purpose"] = 0.8,
        ["cyber_offensive"] = 0.8,
        ["jailbreak_or_injection"] = 0.8,
        ["criminal_activity"] = 0.75,
        ["fraud_deception"] = 0.75,
        ["regulated_goods"] = 0.7,
        ["privacy_surveillance"] = 0.65,
    };

    /// <summary>The weight of a category question with no entry in <see cref="CategoryWeights"/>.</summary>
    public double DefaultCategoryWeight { get; set; } = 0.75;

    /// <summary>
    /// The longest text sent in one call, in characters. Longer text is cut into overlapping pieces, each judged,
    /// and the text is as risky as its riskiest piece, so padding cannot dilute a request.
    /// </summary>
    public int MaxChunkChars { get; set; } = 20_000;

    /// <summary>
    /// The most calls one request may need. Beyond it the request is refused as too large to inspect, whatever
    /// <see cref="WhenUnavailable"/> says: a limit that forwarded what it could not read would be the way around.
    /// </summary>
    public int MaxCallsPerRequest { get; set; } = 48;

    /// <summary>How many calls one request makes at once.</summary>
    public int MaxParallelCalls { get; set; } = 8;

    /// <summary>
    /// How long a text's judgement is remembered, by the SHA-256 of the text (never the text). The whole
    /// conversation rides along in every request, so without this each turn would re-judge every earlier one.
    /// </summary>
    public TimeSpan VerdictTtl { get; set; } = TimeSpan.FromDays(1);

    /// <summary>The most judgements remembered at once.</summary>
    public int VerdictCacheEntries { get; set; } = 20_000;
}
