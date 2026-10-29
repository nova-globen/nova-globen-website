using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace Didin.Tests;

/// <summary>
/// A site's catalogue, imported and read back. The catalogue is made up for the tests: invented titles, people and
/// paths, nothing from any real collection.
/// </summary>
public sealed class CatalogTests : IDisposable
{
    private static readonly DidinSite Site = new(7, "/didin");

    private readonly TestTenants _tenants = new();
    private readonly InMemoryRepository<DidinDocument> _rows;
    private readonly DidinStore _store;
    private readonly CatalogCache _cache = new();
    private readonly string _cacheFolder = Path.Combine(Path.GetTempPath(), "didin-tests-" + Guid.NewGuid().ToString("N"));
    private readonly DidinFiles _files;
    private readonly CatalogImport _import;

    public CatalogTests()
    {
        _rows = new InMemoryRepository<DidinDocument>(_tenants);
        _store = new DidinStore(_rows, _tenants, TimeProvider.System);
        _files = new DidinFiles(_cacheFolder);
        _import = new CatalogImport(_store, _files, TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(_cacheFolder)) Directory.Delete(_cacheFolder, recursive: true);
    }

    private CatalogQuery Query(Dictionary<string, string?>? settings = null) =>
        new(_store, _cache, new ConfigurationBuilder().AddInMemoryCollection(settings ?? new()).Build());

    private static CatalogImportRequest Sample() => new()
    {
        Version = "sample",
        Titles =
        [
            new TitleDoc
            {
                Id = "titles/1", Kind = "movie", Title = "The Lantern Keeper", Year = 1950, Genres = ["Drama", "Mystery", "Romance"],
                People = [new PersonCredit { Name = "Ada Example", Role = "director" }, new PersonCredit { Name = "Bo Sample", Role = "actor", Billing = 1 }],
                Collections = ["Lights"],
            },
            new TitleDoc
            {
                Id = "titles/2", Kind = "movie", Title = "Harbour Lights", Year = 1952, Genres = ["Drama", "Mystery", "Romance"],
                People = [new PersonCredit { Name = "Ada Example", Role = "director" }],
                Collections = ["Lights"],
            },
            new TitleDoc { Id = "titles/3", Kind = "series", Title = "Quiet Street", Year = 1960, Genres = ["Comedy"] },
            new TitleDoc { Id = "titles/4", Kind = "movie", Title = "Nothing Left", Year = 1970, Genres = ["Drama"] },
        ],
        Episodes =
        [
            new EpisodeDoc { Id = "episodes/31", TitleId = 3, Season = 1, Episode = 2, Name = "Second" },
            new EpisodeDoc { Id = "episodes/30", TitleId = 3, Season = 1, Episode = 1, Name = "First" },
        ],
        Files =
        [
            new FileDoc { Id = "files/10", TitleId = 1, FilePath = "media/one.mp4", FileName = "one.mp4", Quality = "720p", Status = "ok",
                          Subtitles = [new SubtitleRef { Path = "media/one.en.srt", Lang = "en" }] },
            new FileDoc { Id = "files/11", TitleId = 1, FilePath = "media/one-hd.mp4", FileName = "one-hd.mp4", Quality = "1080p", Status = "ok" },
            new FileDoc { Id = "files/20", TitleId = 2, FilePath = "media/two.mp4", FileName = "two.mp4", Status = "ok" },
            new FileDoc { Id = "files/30", TitleId = 3, EpisodeId = 30, FilePath = "media/s1e1.mp4", FileName = "s1e1.mp4", Status = "ok" },
            new FileDoc { Id = "files/31", TitleId = 3, EpisodeId = 31, FilePath = "media/s1e2.mp4", FileName = "s1e2.mp4", Status = "ok" },
            new FileDoc { Id = "files/40", TitleId = 4, FilePath = "media/gone.mp4", FileName = "gone.mp4", Status = "missing" },
        ],
        Collections = [new CollectionDoc { Id = "collections/1", Name = "Lights", Members = [new CollectionMember { TitleId = 2, Order = 1 }, new CollectionMember { TitleId = 1, Order = 2 }] }],
    };

    [Fact]
    public async Task The_grid_lists_titles_with_something_to_play()
    {
        (await _import.ImportAsync(Site, Sample())).Ok.Should().BeTrue();

        var catalog = await Query().GetCatalogAsync(Site);

        catalog.Cards.Select(c => c.Id).Should().BeEquivalentTo([1, 2, 3]);   // 4's only file is missing
        catalog.TitleCount.Should().Be(4);
        catalog.Cards.Single(c => c.Id == 1).Quality.Should().Be("1080p");
        catalog.Cards.Single(c => c.Id == 3).EpisodeCount.Should().Be(2);
        catalog.Cards.Single(c => c.Id == 1).Collection.Should().Be("Lights");
        catalog.Facets.Genres.Should().BeEquivalentTo(["Comedy", "Drama", "Mystery", "Romance"]);
        catalog.Version.Should().StartWith("sample-");
    }

    [Fact]
    public async Task A_poster_is_addressed_on_the_site_and_moves_the_version()
    {
        await _import.ImportAsync(Site, Sample());
        var query = Query();
        var before = (await query.GetCatalogAsync(Site)).Version;

        var hash = await _import.ImportPosterAsync(Site, 1, [1, 2, 3, 4], "image/png");
        var after = await query.GetCatalogAsync(Site);

        after.Version.Should().NotBe(before);
        after.Cards.Single(c => c.Id == 1).PosterUrl.Should().Be($"/didin/api/art/1?v={hash}");
        after.Cards.Single(c => c.Id == 2).PosterUrl.Should().BeNull();
        using var stored = _files.Open(CatalogImport.PostersFolder, hash);
        stored.Should().NotBeNull();
    }

    [Fact]
    public async Task A_series_page_has_its_seasons_in_order_with_their_files()
    {
        await _import.ImportAsync(Site, Sample());

        var details = await Query().GetDetailsAsync(Site, 3);

        details!.Seasons.Should().ContainSingle();
        details.Seasons[0].Episodes.Select(e => e.Name).Should().Equal("First", "Second");
        details.Seasons[0].Episodes[0].Files.Should().ContainSingle().Which.FileName.Should().Be("s1e1.mp4");
    }

    [Fact]
    public async Task A_title_page_offers_its_franchise_and_titles_like_it()
    {
        await _import.ImportAsync(Site, Sample());

        var details = await Query().GetDetailsAsync(Site, 1);

        details!.Files.Select(f => f.FileName).Should().BeEquivalentTo(["one.mp4", "one-hd.mp4"]);
        details.Related.Should().ContainSingle().Which.Id.Should().Be(2);
        // 2 shares the director but is in the same franchise: left to the franchise row
        details.Recommended.Should().BeEmpty();
        details.Files.Single(f => f.FileName == "one.mp4").Subtitles.Should().ContainSingle()
            .Which.Url.Should().StartWith("/didin/api/subs/");
    }

    [Fact]
    public async Task A_person_lists_the_titles_they_are_credited_on()
    {
        await _import.ImportAsync(Site, Sample());

        var person = await Query().GetPersonAsync(Site, "ada example");

        person!.Titles.Select(t => t.TitleId).Should().BeEquivalentTo([1, 2]);
        person.Roles.Should().Equal("director");
        (await Query().GetPersonAsync(Site, "Nobody")).Should().BeNull();
    }

    [Fact]
    public async Task Only_a_file_id_names_a_file()
    {
        await _import.ImportAsync(Site, Sample());
        var query = Query();

        (await query.GetFileAsync(Site, "files/10"))!.FileName.Should().Be("one.mp4");
        (await query.GetFileAsync(Site, "titles/1")).Should().BeNull();
        (await query.GetFileAsync(Site, "files/999")).Should().BeNull();
        (await query.GetFileAsync(Site, "files/x")).Should().BeNull();
    }

    [Fact]
    public async Task Another_site_sees_none_of_it()
    {
        await _import.ImportAsync(Site, Sample());

        (await Query().GetCatalogAsync(new DidinSite(8, "/didin"))).Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task An_import_with_a_bad_id_writes_nothing()
    {
        var request = Sample();
        request.Titles.Add(new TitleDoc { Id = "films/5" });
        request.Files.Add(new FileDoc { Id = "files/10" });

        var result = await _import.ImportAsync(Site, request);

        result.Ok.Should().BeFalse();
        result.Problems.Should().HaveCount(2);
        _rows.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_media_store_no_direct_address_is_offered()
    {
        await _import.ImportAsync(Site, Sample());

        Query(new() { ["MediaStore:DirectUrls"] = "true" }).WebDavUrl("media/one.mp4").Should().BeNull();
        Query(new() { ["MediaStore:WebDavBase"] = "https://store.example/dav/" }).WebDavUrl("media/a b.mp4")
            .Should().Be("https://store.example/dav/media/a%20b.mp4");
    }

    [Fact]
    public void The_file_cache_names_content_by_its_hash_and_refuses_anything_else()
    {
        var hash = _files.Save([9, 9, 9], "posters");

        _files.Save([9, 9, 9], "posters").Should().Be(hash);
        hash.Should().HaveLength(32);
        _files.Open("posters", "../../../secrets").Should().BeNull();
        _files.Open("posters", new string('z', 32)).Should().BeNull();
        using var stream = _files.Open("posters", hash);
        stream.Should().NotBeNull();
    }

    /// <summary>
    /// A library read while an import runs sees whatever rows are written so far: titles before their files, files
    /// before their titles' collections, a poster for a title it lists or not. Every one of those reads answers - an
    /// empty or partial catalogue, never an error.
    /// </summary>
    [Fact]
    public async Task A_read_during_an_import_answers_at_every_step()
    {
        var sample = Sample();
        var writes = new List<Func<Task>>();
        foreach (var t in sample.Titles) writes.Add(() => _store.StoreAsync(Site.SiteId, DidinKind.Title, CatalogQuery.NumberOf(t.Id), 0, t));
        foreach (var e in sample.Episodes) writes.Add(() => _store.StoreAsync(Site.SiteId, DidinKind.Episode, CatalogQuery.NumberOf(e.Id), 0, e));
        foreach (var f in sample.Files) writes.Add(() => _store.StoreAsync(Site.SiteId, DidinKind.File, CatalogQuery.NumberOf(f.Id), 0, f));
        foreach (var c in sample.Collections) writes.Add(() => _store.StoreAsync(Site.SiteId, DidinKind.Collection, CatalogQuery.NumberOf(c.Id), 0, c));
        // the files first, as a reader racing a re-import of the files would see them
        writes.Reverse();

        var counts = new List<int>();
        foreach (var write in writes)
        {
            await write();
            // a new cache each time: no version moves until the import ends, and the reader must build afresh
            var catalog = await new CatalogQuery(_store, new CatalogCache(), new ConfigurationBuilder().Build()).GetCatalogAsync(Site);
            counts.Add(catalog.Cards.Count);
        }
        await _import.ImportPosterAsync(Site, 99, [1, 2, 3], "image/png");
        var after = await Query().GetCatalogAsync(Site);

        counts.Should().HaveCount(writes.Count);
        after.Cards.Select(c => c.Id).Should().BeEquivalentTo([1, 2, 3]);
    }

    /// <summary>
    /// The old catalogue's documents arrive as they were stored, and a list a title never had may come as null
    /// rather than empty. The grid, its filters and the people index read past it.
    /// </summary>
    [Fact]
    public async Task A_document_with_null_lists_is_read_like_an_empty_one()
    {
        await _store.StoreAsync(Site.SiteId, DidinKind.Title, 50, 0, new TitleDoc
        {
            Id = "titles/50", Kind = "movie", Title = "Bare Title",
            Genres = null!, Languages = null!, Countries = null!, Keywords = null!, People = null!, Collections = null!,
        });
        await _store.StoreAsync(Site.SiteId, DidinKind.File, 500, 0, new FileDoc { Id = "files/500", TitleId = 50, FilePath = "bare.mp4", Status = "ok", Subtitles = null! });
        await _store.StoreAsync(Site.SiteId, DidinKind.Collection, 5, 0, new CollectionDoc { Id = "collections/5", Name = "Empty", Members = null! });

        var catalog = await Query().GetCatalogAsync(Site);

        catalog.Cards.Should().ContainSingle(c => c.Id == 50);
    }

    /// <summary>
    /// A file whose EpisodeId still names another series' episode (the old catalogue holds such links) plays its own
    /// series' episode with the same season and number; one whose link finds nothing is not offered, and no other
    /// series gains it.
    /// </summary>
    [Fact]
    public async Task A_file_linked_to_another_series_episode_plays_its_own_series_episode()
    {
        await _import.ImportAsync(Site, new CatalogImportRequest
        {
            Version = "relinked",
            Titles =
            [
                new TitleDoc { Id = "titles/60", Kind = "series", Title = "Second Match", Year = 2001 },
                new TitleDoc { Id = "titles/61", Kind = "series", Title = "First Match", Year = 1981 },
            ],
            Episodes =
            [
                new EpisodeDoc { Id = "episodes/600", TitleId = 60, Season = 1, Episode = 1, Name = "One" },
                new EpisodeDoc { Id = "episodes/601", TitleId = 60, Season = 1, Episode = 2, Name = "Two" },
                new EpisodeDoc { Id = "episodes/610", TitleId = 61, Season = 1, Episode = 1, Name = "Uno" },
                new EpisodeDoc { Id = "episodes/611", TitleId = 61, Season = 1, Episode = 2, Name = "Dos" },
                new EpisodeDoc { Id = "episodes/612", TitleId = 61, Season = 2, Episode = 9, Name = "Nine" },
            ],
            Files =
            [
                // series 60's files, still linked to series 61's episodes
                new FileDoc { Id = "files/6000", TitleId = 60, EpisodeId = 610, FilePath = "s/one.mp4", Status = "indexed", Playback = "native" },
                new FileDoc { Id = "files/6001", TitleId = 60, EpisodeId = 611, FilePath = "s/two.mp4", Status = "indexed", Playback = "native" },
                // a link to an episode the series does not have
                new FileDoc { Id = "files/6002", TitleId = 60, EpisodeId = 612, FilePath = "s/nine.mp4", Status = "indexed", Playback = "native" },
                new FileDoc { Id = "files/6100", TitleId = 61, EpisodeId = 610, FilePath = "f/uno.mp4", Status = "indexed", Playback = "native" },
            ],
        });
        var query = Query();

        var second = await query.GetDetailsAsync(Site, 60);
        var first = await query.GetDetailsAsync(Site, 61);

        second.Seasons.SelectMany(s => s.Episodes).Select(e => (e.Number, e.Files.Count)).Should().Equal((1, 1), (2, 1));
        first.Seasons.SelectMany(s => s.Episodes).Sum(e => e.Files.Count).Should().Be(1);
    }
}
