using System.Security.Claims;
using System.Text.Json;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Didin.Tests;

/// <summary>
/// The public sample: files on a public https store the player is sent to, a catalogue anyone may browse on a host
/// that says so, each title's credit and licence, and a catalogue the host seeds into an empty site. Invented titles.
/// </summary>
public sealed class SampleTests : IDisposable
{
    private static readonly DidinSite Site = new(7, "/didin");

    private readonly TestTenants _tenants = new();
    private readonly DidinStore _store;
    private readonly CatalogImport _import;
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "didin-sample-" + Guid.NewGuid().ToString("N"));

    public SampleTests()
    {
        _store = new DidinStore(new InMemoryRepository<DidinDocument>(_tenants), _tenants, TimeProvider.System);
        _import = new CatalogImport(_store, new DidinFiles(Path.Combine(_folder, "cache")), TimeProvider.System);
        Directory.CreateDirectory(Path.Combine(_folder, "posters"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    // ------------------------------------------------------------------ the public store

    [Fact]
    public void A_public_store_gives_each_file_its_own_escaped_https_address()
    {
        var source = new MediaSource(Config(new() { ["MediaStore:PublicBase"] = "https://media.example.test/" }));

        source.IsPublic.Should().BeTrue();
        source.IsLocal.Should().BeFalse();
        source.IsWebDav.Should().BeFalse();
        source.PublicUrl("films/a film/one #1.mp4").Should().Be("https://media.example.test/films/a%20film/one%20%231.mp4");
        source.PublicUrl("films\\two.mp4").Should().Be("https://media.example.test/films/two.mp4");
    }

    [Theory]
    [InlineData("../secret.mp4")]
    [InlineData("films/../../secret.mp4")]
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void A_path_that_climbs_or_is_rooted_has_no_public_address(string path)
    {
        var source = new MediaSource(Config(new() { ["MediaStore:PublicBase"] = "https://media.example.test" }));

        source.PublicUrl(path).Should().BeNull();
    }

    [Theory]
    [InlineData("http://media.example.test/")]
    [InlineData("media.example.test")]
    public void A_public_store_is_https_or_the_host_does_not_start(string value)
    {
        var start = () => new MediaSource(Config(new() { ["MediaStore:PublicBase"] = value }));

        start.Should().Throw<InvalidOperationException>().WithMessage("*https*");
    }

    [Fact]
    public void A_local_folder_still_wins_over_a_public_store()
    {
        var source = new MediaSource(Config(new() { ["MediaStore:Root"] = _folder, ["MediaStore:PublicBase"] = "https://media.example.test" }));

        source.IsLocal.Should().BeTrue();
        source.IsPublic.Should().BeFalse();
    }

    // ------------------------------------------------------------------ who may browse

    private static async Task<bool> MayBrowse(ClaimsPrincipal user, string? publicCatalog)
    {
        var handler = new DidinBrowsing.Handler(Config(new() { [DidinBrowsing.PublicKey] = publicCatalog }));
        var context = new AuthorizationHandlerContext([new DidinBrowsing.Requirement()], user, null);
        await handler.HandleAsync(context);
        return context.HasSucceeded;
    }

    private static ClaimsPrincipal Anonymous => new(new ClaimsIdentity());
    private static ClaimsPrincipal SignedIn => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "someone")], "cookie"));

    [Fact]
    public async Task Browsing_needs_a_sign_in_unless_the_host_makes_the_catalogue_public()
    {
        (await MayBrowse(Anonymous, null)).Should().BeFalse();
        (await MayBrowse(Anonymous, "false")).Should().BeFalse();
        (await MayBrowse(SignedIn, null)).Should().BeTrue();
        (await MayBrowse(Anonymous, "true")).Should().BeTrue();
        (await MayBrowse(Anonymous, "TRUE")).Should().BeTrue();
    }

    [Fact]
    public void Only_browsing_is_opened_by_a_public_catalogue()
    {
        static string? PolicyOf(Type controller) =>
            controller.GetCustomAttributes(typeof(AuthorizeAttribute), false).Cast<AuthorizeAttribute>().SingleOrDefault()?.Policy;

        var browsing = new[] { "ArtController", "CatalogController", "PeopleController", "TitlesController", "StreamController", "SubsController" };
        var controllers = typeof(DidinBrowsing).Assembly.GetTypes()
            .Where(t => typeof(Microsoft.AspNetCore.Mvc.ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .ToList();

        foreach (var controller in controllers)
        {
            if (browsing.Contains(controller.Name))
            {
                PolicyOf(controller).Should().Be(DidinBrowsing.Policy, controller.Name);
            }
            else
            {
                PolicyOf(controller).Should().NotBe(DidinBrowsing.Policy, $"{controller.Name} is an account's own");
            }
        }
        controllers.Select(c => c.Name).Should().Contain(browsing);
    }

    // ------------------------------------------------------------------ credits

    [Fact]
    public void The_credits_name_each_licensor_of_a_listed_title_with_its_licences_once()
    {
        var credits = CatalogQuery.Credits(
        [
            new TitleDoc { Id = "titles/1", Licensor = "Example Studio", LicensorUrl = "https://studio.example.test", License = "CC BY 3.0", LicenseUrl = "https://creativecommons.org/licenses/by/3.0/" },
            new TitleDoc { Id = "titles/2", Licensor = "example studio", License = "CC BY 4.0", LicenseUrl = "https://creativecommons.org/licenses/by/4.0/" },
            new TitleDoc { Id = "titles/3", Licensor = "Example Studio", License = "CC BY 3.0" },
            new TitleDoc { Id = "titles/4", Title = "No licensor" },
        ]);

        var credit = credits.Should().ContainSingle().Subject;
        credit.Licensor.Should().Be("Example Studio");
        credit.LicensorUrl.Should().Be("https://studio.example.test");
        credit.Licenses.Select(l => l.Name).Should().Equal("CC BY 3.0", "CC BY 4.0");
        credit.Licenses[0].Url.Should().Be("https://creativecommons.org/licenses/by/3.0/");
    }

    [Fact]
    public async Task A_title_page_carries_its_credit_and_the_catalogue_its_credits()
    {
        await _import.ImportAsync(Site, Package());
        var query = new CatalogQuery(_store, new CatalogCache(), Config(new()));

        var details = await query.GetDetailsAsync(Site, 1);
        var catalog = await query.GetCatalogAsync(Site);

        details.Attribution.Should().Be("(c) Example Studio");
        details.License.Should().Be("CC BY 4.0");
        details.LicenseUrl.Should().Be("https://creativecommons.org/licenses/by/4.0/");
        details.SourceUrl.Should().Be("https://studio.example.test/licence");
        catalog.Credits.Should().ContainSingle().Which.Licensor.Should().Be("Example Studio");
    }

    // ------------------------------------------------------------------ the seed

    private static CatalogImportRequest Package() => new()
    {
        Version = "sample-test",
        Titles =
        [
            new TitleDoc
            {
                Id = "titles/1", Kind = "movie", Title = "Paper Moon Garden", Year = 2001, RuntimeMinutes = 9,
                Licensor = "Example Studio", LicensorUrl = "https://studio.example.test", Attribution = "(c) Example Studio",
                License = "CC BY 4.0", LicenseUrl = "https://creativecommons.org/licenses/by/4.0/", SourceUrl = "https://studio.example.test/licence",
            },
            new TitleDoc { Id = "titles/2", Kind = "movie", Title = "Glass Harbour", Year = 2003, RuntimeMinutes = 12 },
        ],
        Files =
        [
            new FileDoc { Id = "files/1", TitleId = 1, FilePath = "films/paper-moon-garden/paper-moon-garden-1080p.mp4", Status = "indexed", Playback = "native" },
            new FileDoc { Id = "files/2", TitleId = 2, FilePath = "films/glass-harbour/glass-harbour-1080p.mp4", Status = "indexed", Playback = "native" },
        ],
    };

    private string WriteSeed(object package)
    {
        var path = Path.Combine(_folder, "catalog.json");
        File.WriteAllText(path, JsonSerializer.Serialize(package, DidinStore.Json));
        File.WriteAllBytes(Path.Combine(_folder, "posters", "1.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        return path;
    }

    private CatalogSeed Seed(string? source) =>
        new(Config(new() { [CatalogSeed.Key] = source }), _store, _import, NullLogger<CatalogSeed>.Instance);

    [Fact]
    public async Task An_empty_catalogue_is_seeded_with_its_titles_files_and_the_posters_there_are()
    {
        var said = await Seed(WriteSeed(Package())).SeedIfEmptyAsync(Site);

        said.Should().Contain("2 titles").And.Contain("2 files").And.Contain("1 posters");
        (await _store.AllAsync<TitleDoc>(Site.SiteId, DidinKind.Title)).Should().HaveCount(2);
        (await _store.LoadAsync<PosterDoc>(Site.SiteId, DidinKind.Poster, 1)).Should().NotBeNull();
        (await _store.LoadAsync<PosterDoc>(Site.SiteId, DidinKind.Poster, 2)).Should().BeNull();
    }

    [Fact]
    public async Task A_catalogue_with_anything_in_it_is_never_seeded()
    {
        await _import.ImportAsync(Site, new CatalogImportRequest
        {
            Version = "owner",
            Titles = [new TitleDoc { Id = "titles/40", Kind = "movie", Title = "Already Here" }],
        });

        (await Seed(WriteSeed(Package())).SeedIfEmptyAsync(Site)).Should().BeNull();

        (await _store.AllAsync<TitleDoc>(Site.SiteId, DidinKind.Title)).Select(t => t.Title).Should().Equal("Already Here");
    }

    [Fact]
    public async Task A_bad_package_is_refused_whole_and_nothing_is_stored()
    {
        var bad = Package();
        bad.Titles.Add(new TitleDoc { Id = "", Title = "No id" });

        (await Seed(WriteSeed(bad)).SeedIfEmptyAsync(Site)).Should().BeNull();

        (await _store.AllAsync<TitleDoc>(Site.SiteId, DidinKind.Title)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task No_seed_set_does_nothing(string? source)
    {
        (await Seed(source).SeedIfEmptyAsync(Site)).Should().BeNull();
        (await _store.AllAsync<TitleDoc>(Site.SiteId, DidinKind.Title)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_seed_that_cannot_be_read_leaves_the_catalogue_empty()
    {
        (await Seed(Path.Combine(_folder, "missing.json")).SeedIfEmptyAsync(Site)).Should().BeNull();
        (await Seed("http://media.example.test/catalog.json").SeedIfEmptyAsync(Site)).Should().BeNull();

        (await _store.AllAsync<TitleDoc>(Site.SiteId, DidinKind.Title)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://media.example.test/catalog.json", "posters/3.jpg", "https://media.example.test/posters/3.jpg")]
    [InlineData("https://media.example.test/sample/catalog.json", "posters/3.jpg", "https://media.example.test/sample/posters/3.jpg")]
    public void A_poster_is_looked_for_beside_the_catalogue(string source, string relative, string expected)
    {
        CatalogSeed.Beside(source, relative).Should().Be(expected);
    }

    // ------------------------------------------------------------------ wide art

    private static byte[] Jpeg(int width, int height) =>
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00,
        0xFF, 0xC0, 0x00, 0x0B, 0x08, (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width, 0x01, 0x01, 0x11, 0x00,
        0xFF, 0xD9, 0x00, 0x00, 0x00,
    ];

    private static byte[] Png(int width, int height)
    {
        var b = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 }.CopyTo(b, 0);
        b[16] = (byte)(width >> 24); b[17] = (byte)(width >> 16); b[18] = (byte)(width >> 8); b[19] = (byte)width;
        b[20] = (byte)(height >> 24); b[21] = (byte)(height >> 16); b[22] = (byte)(height >> 8); b[23] = (byte)height;
        return b;
    }

    [Fact]
    public void An_image_header_gives_its_size()
    {
        ImageSize.Read(Jpeg(1920, 818)).Should().Be((1920, 818));
        ImageSize.Read(Png(600, 900)).Should().Be((600, 900));
        ImageSize.Read([1, 2, 3]).Should().BeNull();
        ImageSize.Read(new byte[40]).Should().BeNull();
    }

    [Fact]
    public async Task Wide_art_is_marked_so_the_card_shows_it_whole_and_a_portrait_poster_is_not()
    {
        await _import.ImportAsync(Site, Package());
        await _import.ImportPosterAsync(Site, 1, Jpeg(1920, 818), "image/jpeg");
        await _import.ImportPosterAsync(Site, 2, Png(600, 900), "image/png");
        var query = new CatalogQuery(_store, new CatalogCache(), Config(new()));

        var cards = (await query.GetCatalogAsync(Site)).Cards.ToDictionary(c => c.Id);

        cards[1].PosterWide.Should().BeTrue();
        cards[2].PosterWide.Should().BeFalse();
        (await query.GetDetailsAsync(Site, 1)).PosterWide.Should().BeTrue();
    }
}
