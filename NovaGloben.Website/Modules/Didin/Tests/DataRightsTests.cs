using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Stomme.Erasure;
using Stomme.Portability;

namespace Didin.Tests;

/// <summary>A person deleted from the Didin site: their Didin data goes with them, and their data package holds it.</summary>
public sealed class DataRightsTests
{
    private const int SiteId = 7;
    private readonly TestTenants _tenants = new();
    private readonly DidinStore _store;

    public DataRightsTests() => _store = new DidinStore(new InMemoryRepository<DidinDocument>(_tenants), _tenants, TimeProvider.System);

    private async Task SeedAsync()
    {
        foreach (var user in new[] { 5, 6 })
        {
            await _store.StoreAsync(SiteId, DidinKind.UserState, user, 0, new UserStateDoc { UserId = user, Watchlist = [1, 2], SubtitleLang = "sv" });
            await _store.StoreAsync(SiteId, DidinKind.Rating, 1, user, new RatingDoc { TitleId = 1, UserId = user, UserName = $"user{user}", Stars = 4, Review = "Good." });
        }
        await _store.StoreAsync(SiteId, DidinKind.Rating, 2, 5, new RatingDoc { TitleId = 2, UserId = 5, UserName = "user5", Stars = 2 });
        await _store.StoreAsync(SiteId, DidinKind.ReviewVerdict, 10, 0, new ReviewVerdictDoc { FileId = "files/10", Verdict = "note", Note = "a trailer", UserName = "user5" });
    }

    [Fact]
    public async Task Deleting_a_person_removes_their_list_and_ratings_and_unnames_their_verdicts_only()
    {
        await SeedAsync();
        var erasure = new DidinUserErasure(_store);

        var outcomes = await erasure.EraseAsync(new ErasureSubject(SiteId, 5, null, "user5"));

        outcomes.Select(o => (o.Store, o.Handling, o.Affected)).Should().Equal(
            (DidinUserErasure.UserStateStore, ErasureHandling.Delete, 1),
            (DidinUserErasure.RatingsStore, ErasureHandling.Delete, 2),
            (DidinUserErasure.VerdictsStore, ErasureHandling.Anonymise, 1));
        (await _store.LoadAsync<UserStateDoc>(SiteId, DidinKind.UserState, 5)).Should().BeNull();
        (await _store.AllAsync<RatingDoc>(SiteId, DidinKind.Rating)).Select(r => r.UserId).Should().Equal(6);
        (await _store.LoadAsync<ReviewVerdictDoc>(SiteId, DidinKind.ReviewVerdict, 10))!.UserName.Should().Be(DidinUserErasure.ErasedName);
        (await _store.LoadAsync<UserStateDoc>(SiteId, DidinKind.UserState, 6)).Should().NotBeNull("another person's data stays");

        (await erasure.EraseAsync(new ErasureSubject(SiteId, 5, null, "user5"))).Sum(o => o.Affected).Should().Be(0, "a second run finds nothing");
    }

    [Fact]
    public async Task A_data_package_holds_the_persons_list_and_ratings_and_nobody_elses()
    {
        await SeedAsync();

        var entities = await new DidinSubjectData(_store).ExportForSubjectAsync(new SubjectScope(SiteId, 5));

        entities.Select(e => e.Ref.EntityType).Should().Equal("didin-user-state", "didin-rating", "didin-rating");
        JsonDocument.Parse(entities[0].PayloadJson).RootElement.GetProperty("watchlist").GetArrayLength().Should().Be(2);
        entities.Skip(1).Should().OnlyContain(e => !e.PayloadJson.Contains("user6"));
    }
}