using Stomme.Persistence.Abstractions;
using Stomme.SharedKernel.Domain;
using Stomme.Tenancy.Domain;

namespace Delato.Server.Storage;

/// <summary>The kinds of document Delato keeps. Stored as a number; never renumber one.</summary>
public enum DelatoKind
{
    /// <summary>A group with its members and couples/subgroups. Number = group id.</summary>
    Group = 1,

    /// <summary>An expense with its targets and shares. Number = group id, Owner = expense id.</summary>
    Expense = 2,

    /// <summary>A recorded payment between two members. Number = group id, Owner = settlement id.</summary>
    Settlement = 3,

    /// <summary>The site's last issued ids, one sequence per kind of id. Number = 0.</summary>
    Ids = 4,
}

/// <summary>
/// One of Delato's documents on one site: a group, an expense, a settlement or the site's id counters. The document
/// itself is JSON; the row carries what reads filter on, all of it numbers, which every store compares alike.
/// </summary>
/// <remarks>
/// One aggregate for every kind, as the Didin module keeps its catalogue: a group is read whole with its expenses and
/// settlements, so no read needs a field as a column, and the module store never renames or drops a column.
/// Tenant-owned and site-scoped: every read filters by <see cref="SiteId"/> as well, because the tenant filter does
/// not separate sites. Money inside the JSON is written as JSON numbers from <see cref="decimal"/> and read back into
/// <see cref="decimal"/>, which keeps every digit (the store tests prove it).
/// </remarks>
public sealed class DelatoDocument : AggregateRoot<Guid>, ITenantOwned
{
    // Materialisation constructor - every store builds the aggregate through it. Not for application use.
    private DelatoDocument()
    {
        TenantId = null!;
        Json = string.Empty;
    }

    public TenantId TenantId { get; private set; }

    public int SiteId { get; private set; }

    public DelatoKind Kind { get; private set; }

    /// <summary>The document's number within its kind: the group it belongs to (or is); 0 for the id counters.</summary>
    public int Number { get; private set; }

    /// <summary>The second part of the document's identity: an expense's or settlement's own id; else 0.</summary>
    public int Owner { get; private set; }

    public string Json { get; private set; }

    public DateTime UpdatedUtc { get; private set; }

    public static DelatoDocument Create(Guid id, TenantId tenantId, int siteId, DelatoKind kind, int number, int owner, string json, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        if (siteId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(siteId), "A Delato document belongs to a site.");
        }
        return new DelatoDocument
        {
            Id = id,
            TenantId = tenantId,
            SiteId = siteId,
            Kind = kind,
            Number = number,
            Owner = owner,
            Json = json ?? string.Empty,
            UpdatedUtc = nowUtc,
        };
    }

    /// <summary>Replaces the document's content.</summary>
    public void Replace(string json, DateTime nowUtc)
    {
        Json = json ?? string.Empty;
        UpdatedUtc = nowUtc;
    }
}
