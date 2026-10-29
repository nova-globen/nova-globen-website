using Didin.Server.Documents;
using Didin.Server.Storage;
using Stomme.Tenancy.Domain;

namespace Didin.Tests;

/// <summary>Didin's documents on the module store: one per identity, per site, per tenant.</summary>
public sealed class DidinStoreTests
{
    private readonly TestTenants _tenants = new();
    private readonly InMemoryRepository<DidinDocument> _rows;
    private readonly DidinStore _store;

    public DidinStoreTests()
    {
        _rows = new InMemoryRepository<DidinDocument>(_tenants);
        _store = new DidinStore(_rows, _tenants, TimeProvider.System);
    }

    [Fact]
    public async Task A_document_is_written_once_and_replaced_after()
    {
        await _store.StoreAsync(1, DidinKind.Title, 12, 0, new TitleDoc { Id = "titles/12", Title = "First" });
        await _store.StoreAsync(1, DidinKind.Title, 12, 0, new TitleDoc { Id = "titles/12", Title = "Second" });

        _rows.Rows.Should().ContainSingle();
        (await _store.LoadAsync<TitleDoc>(1, DidinKind.Title, 12))!.Title.Should().Be("Second");
    }

    [Fact]
    public async Task Another_site_of_the_tenant_reads_nothing_of_it()
    {
        await _store.StoreAsync(1, DidinKind.Title, 12, 0, new TitleDoc { Id = "titles/12", Title = "Here" });

        (await _store.LoadAsync<TitleDoc>(2, DidinKind.Title, 12)).Should().BeNull();
        (await _store.AllAsync<TitleDoc>(2, DidinKind.Title)).Should().BeEmpty();
    }

    [Fact]
    public async Task Another_tenant_reads_nothing_of_it()
    {
        await _store.StoreAsync(1, DidinKind.Title, 12, 0, new TitleDoc { Id = "titles/12", Title = "Here" });
        _tenants.CurrentTenantId = TenantId.From(Guid.NewGuid());

        (await _store.LoadAsync<TitleDoc>(1, DidinKind.Title, 12)).Should().BeNull();
    }

    [Fact]
    public async Task Kinds_and_owners_are_kept_apart()
    {
        await _store.StoreAsync(1, DidinKind.Title, 7, 0, new TitleDoc { Id = "titles/7", Title = "A title" });
        await _store.StoreAsync(1, DidinKind.Rating, 7, 100, new RatingDoc { TitleId = 7, UserId = 100, Stars = 4 });
        await _store.StoreAsync(1, DidinKind.Rating, 7, 101, new RatingDoc { TitleId = 7, UserId = 101, Stars = 2 });

        (await _store.AllWithNumberAsync<RatingDoc>(1, DidinKind.Rating, 7)).Select(r => r.Stars).Should().BeEquivalentTo([4, 2]);
        (await _store.LoadAsync<RatingDoc>(1, DidinKind.Rating, 7, 101))!.Stars.Should().Be(2);
        (await _store.AllAsync<TitleDoc>(1, DidinKind.Title)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_document_can_be_removed()
    {
        await _store.StoreAsync(1, DidinKind.ReviewVerdict, 5, 0, new ReviewVerdictDoc { FileId = "files/5", Verdict = "note" });
        await _store.DeleteAsync(1, DidinKind.ReviewVerdict, 5);

        (await _store.LoadAsync<ReviewVerdictDoc>(1, DidinKind.ReviewVerdict, 5)).Should().BeNull();
        _rows.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Nothing_is_written_without_a_tenant()
    {
        _tenants.CurrentTenantId = null;

        var write = () => _store.StoreAsync(1, DidinKind.Title, 1, 0, new TitleDoc { Id = "titles/1" });

        await write.Should().ThrowAsync<InvalidOperationException>();
        _rows.Rows.Should().BeEmpty();
    }

    [Fact]
    public void A_document_needs_a_site() =>
        FluentActions.Invoking(() => DidinDocument.Create(Guid.NewGuid(), TenantId.From(Guid.NewGuid()), 0, DidinKind.Title, 1, 0, "{}", DateTime.UtcNow))
            .Should().Throw<ArgumentOutOfRangeException>();
}
