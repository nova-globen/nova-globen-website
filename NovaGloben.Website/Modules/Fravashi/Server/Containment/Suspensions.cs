using System.Collections.Concurrent;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.Containment;

/// <summary>A user the broker has stopped serving, and enough about it for an operator to act.</summary>
/// <param name="Since">When it began.</param>
/// <param name="Refusals">How many refusals earned it.</param>
/// <param name="Reason">The last refusal's reason, which is what says what kind of trouble it was.</param>
public sealed record Suspension(DateTimeOffset Since, int Refusals, string Reason);

/// <summary>
/// Stopping a user who keeps hitting a wall. Metering refuses a request at a time and never
/// notices a pattern: a loop can spend the whole day being refused, which costs the provider nothing but is
/// exactly the shape of misuse an operator wants to know about.
/// <para>
/// Two decisions worth stating. The window bounds how FAST refusals must arrive to earn a suspension, not how
/// long one lasts: a suspension that expired by itself would let the same loop start again unattended, so
/// lifting it is a person's act. And an allowed request does not clear the count, because a loop that
/// alternates between allowed and refused calls would otherwise never be seen at all.
/// </para>
/// <para>
/// In memory, like the rest of this broker's counters: a restart forgets suspensions, which is a
/// way to clear one for anybody who can cause a restart. Said here rather than discovered.
/// </para>
/// </summary>
public sealed class Suspensions
{
    private readonly BrokerServiceOptions _options;
    private readonly TimeProvider _clock;
    private readonly ConcurrentDictionary<string, Record> _users = new(StringComparer.Ordinal);

    public Suspensions(IOptions<BrokerServiceOptions> options, TimeProvider? clock = null)
    {
        _options = options.Value;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Notes that a user was refused, and suspends them when that is enough of them fast enough.</summary>
    public void Refused(string userId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (_options.SuspendAfterRefusals <= 0)
        {
            return;
        }

        var record = _users.GetOrAdd(userId, _ => new Record());
        var now = _clock.GetUtcNow();
        lock (record)
        {
            if (now - record.WindowStart >= _options.SuspensionWindow)
            {
                record.WindowStart = now;
                record.Refusals = 0;
            }

            record.Refusals++;
            record.LastReason = reason;
            if (record.Refusals >= _options.SuspendAfterRefusals && record.Suspension is null)
            {
                record.Suspension = new Suspension(now, record.Refusals, reason);
            }
        }
    }

    /// <summary>The user's suspension, or null when they are not suspended.</summary>
    public Suspension? Of(string userId) =>
        _users.TryGetValue(userId, out var record) ? Read(record, r => r.Suspension) : null;

    /// <summary>How many refusals the user has in the current window.</summary>
    public int RefusalsOf(string userId) =>
        _users.TryGetValue(userId, out var record) ? Read(record, r => r.Refusals) : 0;

    /// <summary>Everybody currently suspended, for an operator's view.</summary>
    public IReadOnlyList<(string User, Suspension Suspension)> All() =>
        [.. _users
            .Select(pair => (pair.Key, Suspension: Read(pair.Value, r => r.Suspension)))
            .Where(pair => pair.Suspension is not null)
            .Select(pair => (pair.Key, pair.Suspension!))];

    /// <summary>Lifts a suspension, and starts the count again. False when there was none to lift.</summary>
    public bool Lift(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (!_users.TryGetValue(userId, out var record))
        {
            return false;
        }

        lock (record)
        {
            if (record.Suspension is null)
            {
                return false;
            }

            // The refusals that earned it are not counted again: leaving them would suspend the user once
            // more on their very next refusal, which is not what lifting means.
            record.Suspension = null;
            record.Refusals = 0;
            record.WindowStart = _clock.GetUtcNow();
            return true;
        }
    }

    private static T Read<T>(Record record, Func<Record, T> of)
    {
        lock (record)
        {
            return of(record);
        }
    }

    private sealed class Record
    {
        public DateTimeOffset WindowStart { get; set; } = DateTimeOffset.MinValue;

        public int Refusals { get; set; }

        public string LastReason { get; set; } = string.Empty;

        public Suspension? Suspension { get; set; }
    }
}
