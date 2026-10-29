namespace Fravashi.Broker.RateLimiting;

/// <summary>
/// Per-user rate limit + quota enforcement. Both are checked <b>before</b> a request is forwarded,
/// so an over-limit user never reaches the org key. In v1 the quota is a request budget over a window; token-based
/// quota is a documented follow-up. Memory-only, consistent with holding secrets in memory only (counters reset on restart).
/// </summary>
public interface IUsageMeter
{
    /// <summary>
    /// Atomically checks the rate limit then the quota for a user and, when allowed, records the request against
    /// both windows. A denied request is <b>not</b> recorded against the quota (only allowed requests consume the
    /// budget); the rate-limit window still counts the attempt.
    /// </summary>
    UsageDecision TryConsume(string userId);
}
