using Delato.Server.Storage;
using Delato.Shared.Domain;
using Stomme.Tenancy.Domain;

namespace Delato.Tests;

/// <summary>Delato's documents on the module store: one per identity, per site, per tenant.</summary>
public sealed class DelatoStoreTests
{
    private readonly TestTenants _tenants = new();
    private readonly InMemoryRepository<DelatoDocument> _rows;
    private readonly DelatoStore _store;

    public DelatoStoreTests()
    {
        _rows = new InMemoryRepository<DelatoDocument>(_tenants);
        _store = new DelatoStore(_rows, _tenants, TimeProvider.System);
    }

    private static Group NewGroup(int id, string name) => new() { Id = id, Name = name };

    [Fact]
    public async Task A_document_is_written_once_and_replaced_after()
    {
        await _store.StoreAsync(1, DelatoKind.Group, 12, 0, NewGroup(12, "First"));
        await _store.StoreAsync(1, DelatoKind.Group, 12, 0, NewGroup(12, "Second"));

        _rows.Rows.Should().ContainSingle();
        (await _store.LoadAsync<Group>(1, DelatoKind.Group, 12))!.Name.Should().Be("Second");
    }

    [Fact]
    public async Task Another_site_of_the_tenant_reads_nothing_of_it()
    {
        await _store.StoreAsync(1, DelatoKind.Group, 12, 0, NewGroup(12, "Here"));

        (await _store.LoadAsync<Group>(2, DelatoKind.Group, 12)).Should().BeNull();
        (await _store.AllAsync<Group>(2, DelatoKind.Group)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_tenant_reads_nothing_of_it()
    {
        await _store.StoreAsync(1, DelatoKind.Group, 12, 0, NewGroup(12, "Here"));
        _tenants.CurrentTenantId = TenantId.From(Guid.NewGuid());

        (await _store.LoadAsync<Group>(1, DelatoKind.Group, 12)).Should().BeNull();
    }

    [Fact]
    public async Task Kinds_and_owners_are_kept_apart()
    {
        await _store.StoreAsync(1, DelatoKind.Group, 7, 0, NewGroup(7, "A group"));
        await _store.StoreAsync(1, DelatoKind.Settlement, 7, 100, new Settlement { Id = 100, GroupId = 7, CurrencyCode = "USD", AmountUsd = 4m });
        await _store.StoreAsync(1, DelatoKind.Settlement, 7, 101, new Settlement { Id = 101, GroupId = 7, CurrencyCode = "USD", AmountUsd = 2m });

        (await _store.AllWithNumberAsync<Settlement>(1, DelatoKind.Settlement, 7)).Select(s => s.AmountUsd).Should().BeEquivalentTo([4m, 2m]);
        (await _store.LoadAsync<Settlement>(1, DelatoKind.Settlement, 7, 101))!.AmountUsd.Should().Be(2m);
        (await _store.AllAsync<Group>(1, DelatoKind.Group)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_document_can_be_removed()
    {
        await _store.StoreAsync(1, DelatoKind.Settlement, 5, 9, new Settlement { Id = 9, GroupId = 5, CurrencyCode = "USD" });
        await _store.DeleteAsync(1, DelatoKind.Settlement, 5, 9);

        (await _store.LoadAsync<Settlement>(1, DelatoKind.Settlement, 5, 9)).Should().BeNull();
        _rows.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_written_without_a_tenant()
    {
        _tenants.CurrentTenantId = null;

        var write = () => _store.StoreAsync(1, DelatoKind.Group, 1, 0, NewGroup(1, "None"));

        await write.Should().ThrowAsync<InvalidOperationException>();
        _rows.Rows.Should().BeEmpty();
    }

    [Fact]
    public void A_document_needs_a_site() =>
        FluentActions.Invoking(() => DelatoDocument.Create(Guid.NewGuid(), TenantId.From(Guid.NewGuid()), 0, DelatoKind.Group, 1, 0, "{}", DateTime.UtcNow))
            .Should().Throw<ArgumentOutOfRangeException>();

    [Fact]
    public async Task Two_rows_for_one_document_read_as_its_newest_once()
    {
        // two writes at once each found no row and added one
        var tenant = _tenants.CurrentTenantId!;
        await _rows.AddAsync(DelatoDocument.Create(Guid.NewGuid(), tenant, 1, DelatoKind.Group, 7, 0, "{\"id\":7,\"name\":\"Old\"}", new DateTime(2026, 10, 29, 10, 0, 0, DateTimeKind.Utc)));
        await _rows.AddAsync(DelatoDocument.Create(Guid.NewGuid(), tenant, 1, DelatoKind.Group, 7, 0, "{\"id\":7,\"name\":\"New\"}", new DateTime(2026, 10, 29, 10, 0, 1, DateTimeKind.Utc)));

        (await _store.AllAsync<Group>(1, DelatoKind.Group)).Should().ContainSingle().Which.Name.Should().Be("New");
        (await _store.AllWithNumberAsync<Group>(1, DelatoKind.Group, 7)).Should().ContainSingle();
        (await _store.LoadAsync<Group>(1, DelatoKind.Group, 7))!.Name.Should().Be("New");
    }

    [Fact]
    public async Task A_list_stored_as_null_reads_as_empty()
    {
        var tenant = _tenants.CurrentTenantId!;
        await _rows.AddAsync(DelatoDocument.Create(Guid.NewGuid(), tenant, 1, DelatoKind.Group, 3, 0,
            "{\"id\":3,\"name\":\"Bare\",\"members\":null,\"entities\":null}", DateTime.UtcNow));

        var group = (await _store.LoadAsync<Group>(1, DelatoKind.Group, 3))!;

        group.Members.Should().NotBeNull().And.BeEmpty();
        group.Entities.Should().NotBeNull().And.BeEmpty();
    }
}
