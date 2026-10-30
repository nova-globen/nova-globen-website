using System.Collections.Concurrent;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.Containment;

/// <summary>Whether a user may spend more right now, and when they may again if not.</summary>
/// <param name="Allowed">True when both the user's budget and the organisation's have room.</param>
/// <param name="Limit">Which budget is spent: <c>user</c> or <c>organisation</c>; null when allowed.</param>
/// <param name="ResetsAt">When that budget's window turns; null when allowed.</param>
public sealed record BudgetDecision(bool Allowed, string? Limit, DateTimeOffset? ResetsAt)
{
    /// <summary>There is room.</summary>
    public static BudgetDecision Ok { get; } = new(true, null, null);
}

/// <summary>
/// Budgets charged in the provider's own token figures, beside the request quota, which counts
/// requests and so cannot see that one request on the dearest model with a very large context costs what a
/// thousand small ones do. Checked BEFORE a request reaches the org key, charged AFTER the answer has passed.
/// </summary>
public interface ITokenBudget
{
    /// <summary>Whether this user may send another request now.</summary>
    BudgetDecision Check(string userId);

    /// <summary>
    /// Charges what an answer used, in weighted units (<see cref="TokenWeights"/>), with its raw token count for the
    /// record. Null units means the figure could not be read, and that is charged at the configured worst case, never
    /// at nothing: a limit that can be slipped by an unreadable answer is no limit.
    /// </summary>
    void Charge(string userId, long? units, long? rawTokens = null);

    /// <summary>The weighted units a user has used in the current window: what the budget is checked against.</summary>
    long UsedBy(string userId);

    /// <summary>The raw tokens behind them, as the provider counted (answers whose usage was unreadable add none).</summary>
    long RawUsedBy(string userId);

    /// <summary>The organisation's weighted units and raw tokens today.</summary>
    (long Units, long RawTokens) OrganisationUsed();

    /// <summary>When the user's current window started; null when they have none running.</summary>
    DateTimeOffset? WindowStartOf(string userId) => null;
}

/// <summary>
/// In-memory fixed windows, one per user and one for the organisation. What it cannot do, stated rather than
/// discovered: it is charged after the fact, so the request that crosses a budget is answered in full and the
/// NEXT one is refused; and the counters live in memory, so a restart forgets them, which makes a
/// restart a way to reset a budget for anybody who can cause one.
/// </summary>
public sealed class InMemoryTokenBudget : ITokenBudget
{
    // The organisation's window shares the dictionary with the users'. Its key starts with a character no
    // identity provider puts in a subject, named as a code point rather than written as an escape, so that no
    // user id can ever be the organisation's.
    private static readonly string Organisation = (char)1 + "organisation";

    private readonly BrokerServiceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Window> _windows = new(StringComparer.Ordinal);

    public InMemoryTokenBudget(IOptions<BrokerServiceOptions> options, TimeProvider? clock = null)
    {
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public BudgetDecision Check(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        if (Spent(Organisation, _options.OrganisationTokensPerDay, TimeSpan.FromDays(1)) is { } organisation)
        {
            return new BudgetDecision(false, "organisation", organisation);
        }

        return Spent(userId, _options.QuotaTokens, _options.QuotaWindow) is { } user
            ? new BudgetDecision(false, "user", user)
            : BudgetDecision.Ok;
    }

    /// <inheritdoc />
    public void Charge(string userId, long? units, long? rawTokens = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var amount = Math.Max(0, units ?? _options.UnreadableUsageCharge);
        var raw = Math.Max(0, rawTokens ?? 0);

        Add(userId, amount, raw, _options.QuotaWindow);
        Add(Organisation, amount, raw, TimeSpan.FromDays(1));
    }

    /// <inheritdoc />
    public long UsedBy(string userId) => Current(userId, _options.QuotaWindow).Units;

    /// <inheritdoc />
    public long RawUsedBy(string userId) => Current(userId, _options.QuotaWindow).Raw;

    /// <inheritdoc />
    public (long Units, long RawTokens) OrganisationUsed() => Current(Organisation, TimeSpan.FromDays(1));

    /// <inheritdoc />
    public DateTimeOffset? WindowStartOf(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!_windows.TryGetValue(userId, out var window)) return null;
        lock (window)
        {
            return _clock.GetUtcNow() - window.Start >= _options.QuotaWindow ? null : window.Start;
        }
    }

    private (long Units, long Raw) Current(string key, TimeSpan length)
    {
        if (!_windows.TryGetValue(key, out var window))
        {
            return (0, 0);
        }

        lock (window)
        {
            return _clock.GetUtcNow() - window.Start >= length ? (0, 0) : (window.Used, window.Raw);
        }
    }

    /// <summary>When the budget's window turns if it is spent; null while there is room or no limit is set.</summary>
    private DateTimeOffset? Spent(string key, long limit, TimeSpan length)
    {
        if (limit <= 0 || !_windows.TryGetValue(key, out var window))
        {
            return null;
        }

        lock (window)
        {
            return _clock.GetUtcNow() - window.Start < length && window.Used >= limit ? window.Start + length : null;
        }
    }

    private void Add(string key, long amount, long raw, TimeSpan length)
    {
        var window = _windows.GetOrAdd(key, _ => new Window());
        var now = _clock.GetUtcNow();
        lock (window)
        {
            if (now - window.Start >= length)
            {
                window.Start = now;
                window.Used = 0;
                window.Raw = 0;
            }

            // Saturating: a worst-case charge added to a large figure must not wrap around into room.
            window.Used = window.Used > long.MaxValue - amount ? long.MaxValue : window.Used + amount;
            window.Raw = window.Raw > long.MaxValue - raw ? long.MaxValue : window.Raw + raw;
        }
    }

    private sealed class Window
    {
        public DateTimeOffset Start { get; set; } = DateTimeOffset.MinValue;

        public long Used { get; set; }

        public long Raw { get; set; }
    }
}
