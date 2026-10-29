using System.Collections.Concurrent;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.RateLimiting;

/// <summary>
/// In-memory, fixed-window <see cref="IUsageMeter"/>. Per user it keeps a short rate-limit window and a longer
/// quota window; both roll when their window elapses. Rate limit is checked first (an over-rate attempt is
/// counted but does not consume quota), then quota (only allowed requests spend the budget). Thread-safe via a
/// per-user lock; counters are memory-only and reset on restart.
/// </summary>
public sealed class InMemoryUsageMeter : IUsageMeter
{
    private readonly BrokerServiceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, UserWindows> _users = new(StringComparer.Ordinal);

    public InMemoryUsageMeter(IOptions<BrokerServiceOptions> options, TimeProvider? clock = null)
    {
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    public UsageDecision TryConsume(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var state = _users.GetOrAdd(userId, _ => new UserWindows());
        var now = _clock.GetUtcNow();

        lock (state)
        {
            // Rate limit (short window). The attempt is counted whether or not it is allowed.
            if (now - state.RateWindowStart >= _options.RateLimitWindow)
            {
                state.RateWindowStart = now;
                state.RateCount = 0;
            }

            state.RateCount++;
            if (state.RateCount > _options.RateLimitRequests)
            {
                return new UsageDecision(UsageOutcome.RateLimited);
            }

            // Quota (long window, a request budget). Only allowed requests consume it.
            if (now - state.QuotaWindowStart >= _options.QuotaWindow)
            {
                state.QuotaWindowStart = now;
                state.QuotaCount = 0;
            }

            if (state.QuotaCount >= _options.QuotaRequests)
            {
                return new UsageDecision(UsageOutcome.QuotaExceeded);
            }

            state.QuotaCount++;
            return new UsageDecision(UsageOutcome.Allowed);
        }
    }

    private sealed class UserWindows
    {
        public DateTimeOffset RateWindowStart { get; set; } = DateTimeOffset.MinValue;
        public int RateCount { get; set; }
        public DateTimeOffset QuotaWindowStart { get; set; } = DateTimeOffset.MinValue;
        public int QuotaCount { get; set; }
    }
}
