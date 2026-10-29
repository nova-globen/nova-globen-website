using Delato.Server.Services;
using Delato.Server.Storage;

namespace Delato.Tests;

/// <summary>A clock a test sets.</summary>
public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>
/// The ledger on an in-memory module store, one tenant, site 1, with a fixed clock. Every person and address here is
/// invented (example.test).
/// </summary>
public sealed class LedgerHost
{
    public const int Site = 1;

    public TestTenants Tenants { get; } = new();
    public InMemoryRepository<DelatoDocument> Rows { get; }
    public DelatoStore Store { get; }
    public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero));
    public DelatoLedger Ledger { get; }

    public LedgerHost()
    {
        Rows = new InMemoryRepository<DelatoDocument>(Tenants);
        Store = new DelatoStore(Rows, Tenants, Clock);
        Ledger = new DelatoLedger(Store, Clock);
    }

    /// <summary>An account with a confirmed e-mail, name@example.test.</summary>
    public static DelatoCaller Confirmed(int userId, string name) => new(userId, name, $"{name.ToLowerInvariant()}@example.test");

    /// <summary>An account whose e-mail is not confirmed: Delato never sees its address.</summary>
    public static DelatoCaller Unconfirmed(int userId, string name) => new(userId, name, null);
}

/// <summary>Reads a ledger outcome as the HTTP answer would carry it.</summary>
public static class OutcomeExtensions
{
    public static T Body<T>(this Outcome outcome, int status = 200)
    {
        outcome.Status.Should().Be(status, "the outcome was {0}", outcome.Body);
        return outcome.Body.Should().BeAssignableTo<T>().Subject;
    }

    public static string ErrorCode(this Outcome outcome, int status)
    {
        outcome.Status.Should().Be(status);
        return outcome.Body.Should().BeOfType<Delato.Shared.Contracts.ErrorEnvelope>().Subject.Error.Code;
    }
}
