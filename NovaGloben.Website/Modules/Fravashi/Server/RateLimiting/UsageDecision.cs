namespace Fravashi.Broker.RateLimiting;

/// <summary>Why a request was allowed or denied by the per-user rate-limit / quota meter.</summary>
public enum UsageOutcome
{
    /// <summary>Under both the rate limit and the quota.</summary>
    Allowed,

    /// <summary>Too many requests within the rate-limit window.</summary>
    RateLimited,

    /// <summary>The per-user quota (request budget) for the current window is exhausted.</summary>
    QuotaExceeded,
}

/// <summary>The meter's verdict for one request.</summary>
/// <param name="Outcome">Allowed / rate-limited / quota-exceeded.</param>
public sealed record UsageDecision(UsageOutcome Outcome)
{
    /// <summary>True only when the request is under both limits.</summary>
    public bool Allowed => Outcome == UsageOutcome.Allowed;

    /// <summary>A short, secret-free reason for the audit log.</summary>
    public string Reason => Outcome.ToString().ToLowerInvariant();
}
