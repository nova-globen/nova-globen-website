namespace Fravashi.Broker.Audit;

/// <summary>
/// One structured, attributable audit line. Carries <b>no secrets</b> - not the org key, not the
/// signing key, not the token value; only the token id (<c>jti</c>) for correlation. Every forward and mint
/// decision emits one, so a customer can attribute, meter, and investigate use.
/// </summary>
/// <param name="Timestamp">When the decision was made (UTC).</param>
/// <param name="Action">The endpoint/action: <c>mint</c>, <c>forward</c>, or <c>revoke</c>.</param>
/// <param name="UserId">The attributed user id (from the validated token or identity), or null when unauthenticated.</param>
/// <param name="TokenId">The token id (<c>jti</c>) involved, or null (e.g. a mint that failed identity).</param>
/// <param name="Model">The requested model (parsed from the forward body), or null.</param>
/// <param name="UsageUnits">Quota units consumed by this request (v1: 1 per allowed forward), or null.</param>
/// <param name="Outcome">The result: <c>allowed</c> or <c>denied</c>.</param>
/// <param name="Reason">A short, secret-free reason (e.g. <c>expired</c>, <c>revoked</c>, <c>ratelimited</c>).</param>
/// <param name="Tokens">
/// What the forward was charged in the provider's own token figures: zero when nothing was generated,
/// null when the answer's usage could not be read and the configured worst case was charged instead.
/// </param>
/// <param name="Inspection">
/// How the prompt inspection came out, e.g. <c>prompt_allowed</c>, <c>prompt_cautioned</c>,
/// <c>prompt_refused</c>; null where nothing was inspected. The prompt itself is never recorded anywhere.
/// </param>
/// <param name="Risk">The execution risk the inspection computed, 0..1; null where there was none.</param>
/// <param name="Flagged">The categories the inspection flagged, comma separated; null where none.</param>
public sealed record AuditRecord(
    DateTimeOffset Timestamp,
    string Action,
    string? UserId,
    string? TokenId,
    string? Model,
    int? UsageUnits,
    string Outcome,
    string Reason,
    long? Tokens = null,
    string? Inspection = null,
    double? Risk = null,
    string? Flagged = null)
{
    public const string OutcomeAllowed = "allowed";
    public const string OutcomeDenied = "denied";
}
