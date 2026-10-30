using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using Fravashi.Broker.RateLimiting;

namespace Fravashi.Broker.Endpoints;

/// <summary>
/// What <c>GET {broker}/broker/usage/me</c> answers a person, read with their own broker token: their figures in the
/// current quota window and nothing about anybody else or the organisation. The counters live in the broker's memory,
/// so <see cref="CountedSinceUtc"/> says from when they count (a restart starts them again), and a window starts at the
/// person's first request in it, not at midnight, so each limit carries the time its window turns.
/// </summary>
/// <param name="AsOfUtc">When the figures were read.</param>
/// <param name="CountedSinceUtc">When this broker started counting (it keeps no counts across a restart).</param>
/// <param name="WindowSeconds">The length of a quota window (<c>Broker:QuotaWindow</c>).</param>
/// <param name="UnitsUsed">Weighted units used in the current window (the broker's TokenWeights).</param>
/// <param name="UnitsLimit">The units allowed per window; null when no unit budget is set.</param>
/// <param name="UnitsResetAtUtc">When the units window turns; null while none is running.</param>
/// <param name="RequestsUsed">Requests made in the current window.</param>
/// <param name="RequestsLimit">The requests allowed per window.</param>
/// <param name="RequestsResetAtUtc">When the requests window turns; null while none is running.</param>
/// <param name="Suspended">True while the person is suspended (only an operator lifts it).</param>
public sealed record PersonalUsage(
    DateTimeOffset AsOfUtc,
    DateTimeOffset CountedSinceUtc,
    long WindowSeconds,
    long UnitsUsed,
    long? UnitsLimit,
    DateTimeOffset? UnitsResetAtUtc,
    int RequestsUsed,
    int RequestsLimit,
    DateTimeOffset? RequestsResetAtUtc,
    bool Suspended)
{
    public static PersonalUsage For(string user, IUsageMeter meter, ITokenBudget budget, Suspensions suspensions,
        BrokerServiceOptions options, DateTimeOffset countedSince, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(user);
        var (requests, requestsStart) = meter.RequestsOf(user);
        var unitsStart = budget.WindowStartOf(user);
        return new PersonalUsage(
            now,
            countedSince,
            (long)options.QuotaWindow.TotalSeconds,
            budget.UsedBy(user),
            options.QuotaTokens > 0 ? options.QuotaTokens : null,
            unitsStart + options.QuotaWindow,
            requests,
            options.QuotaRequests,
            requestsStart + options.QuotaWindow,
            suspensions.Of(user) is not null);
    }
}