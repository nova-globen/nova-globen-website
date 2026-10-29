using System.Linq.Expressions;
using Stomme.SharedKernel.Domain;
using Stomme.Tenancy.Abstractions;
using Stomme.Tenancy.Domain;

namespace Didin.Tests;

/// <summary>
/// A repository over a list, filtered by tenant as the real stores filter an ITenantOwned aggregate, and running the
/// same predicates they are given.
/// </summary>
public sealed class InMemoryRepository<T>(TestTenants tenants) : IRepository<T, Guid>
    where T : AggregateRoot<Guid>, Stomme.Persistence.Abstractions.ITenantOwned
{
    public List<T> Rows { get; } = [];

    private IEnumerable<T> Visible => Rows.Where(r => tenants.CurrentTenantId is { } t && r.TenantId == t);

    public Task<T?> GetByIdAsync(Guid id, CancellationToken ct = default) => Task.FromResult(Visible.FirstOrDefault(r => r.Id == id));

    public Task<IReadOnlyList<T>> GetAllAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<T>>(Visible.ToList());

    public Task AddAsync(T aggregate, CancellationToken ct = default)
    {
        Rows.Add(aggregate);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(T aggregate, CancellationToken ct = default) => Task.CompletedTask;

    public Task DeleteAsync(T aggregate, CancellationToken ct = default)
    {
        Rows.Remove(aggregate);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<T>> FindAsync(Expression<Func<T, bool>> predicate, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<T>>(Visible.Where(predicate.Compile()).ToList());
}

/// <summary>An ambient tenant a test sets.</summary>
public sealed class TestTenants : ITenantContext
{
    public TenantId? CurrentTenantId { get; set; } = TenantId.From(Guid.NewGuid());

    public bool IsSystemScope => false;
}
