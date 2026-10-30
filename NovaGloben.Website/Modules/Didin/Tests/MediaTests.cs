using System.Security.Claims;
using Didin.Server.Controllers;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace Didin.Tests;

/// <summary>Where the media comes from, how it is served, and who may fetch it without a sign-in.</summary>
public sealed class MediaTests : IDisposable
{
    private static readonly DidinSite Site = new(7, "/didin");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "didin-media-" + Guid.NewGuid().ToString("N"));
    private readonly TestTenants _tenants = new();
    private readonly DidinStore _store;
    private readonly CatalogCache _cache = new();

    public MediaTests()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "media", "media"));
        _store = new DidinStore(new InMemoryRepository<DidinDocument>(_tenants), _tenants, TimeProvider.System);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static IConfiguration Settings(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private string Root => Path.Combine(_folder, "media");

    [Fact]
    public void A_host_without_a_media_setting_has_no_source()
    {
        var source = new MediaSource(Settings(new()));

        source.IsLocal.Should().BeFalse();
        source.IsWebDav.Should().BeFalse();
        source.LocalPath("media/one.mp4").Should().BeNull();
    }

    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("media/../../secret.txt")]
    [InlineData("../media2/one.mp4")]          // a sibling folder whose name starts like the root's
    [InlineData("/etc/passwd")]
    [InlineData("")]
    public void A_path_that_leaves_the_root_is_refused(string path)
    {
        new MediaSource(Settings(new() { ["MediaStore:Root"] = Root })).LocalPath(path).Should().BeNull();
    }

    [Fact]
    public void A_path_under_the_root_resolves_inside_it()
    {
        var full = new MediaSource(Settings(new() { ["MediaStore:Root"] = Root })).LocalPath("media/one.mp4");

        full.Should().Be(Path.Combine(Root, "media", "one.mp4"));
    }

    [Fact]
    public void The_WebDAV_password_comes_from_its_file_without_the_trailing_newline()
    {
        var file = Path.Combine(_folder, "password");
        File.WriteAllText(file, "s3cret\n");

        var source = new MediaSource(Settings(new()
        {
            ["MediaStore:WebDavBase"] = "https://store.example/dav",
            ["MediaStore:WebDavUser"] = "reader",
            ["MediaStore:WebDavPasswordFile"] = file,
        }));

        source.IsWebDav.Should().BeTrue();
        source.WebDavAuthorization!.Scheme.Should().Be("Basic");
        System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(source.WebDavAuthorization.Parameter!)).Should().Be("reader:s3cret");
    }

    [Fact]
    public void An_unreadable_password_file_names_the_path_and_never_the_content()
    {
        var missing = Path.Combine(_folder, "no-such-file");

        var act = () => new MediaSource(Settings(new()
        {
            ["MediaStore:WebDavBase"] = "https://store.example/dav",
            ["MediaStore:WebDavUser"] = "reader",
            ["MediaStore:WebDavPasswordFile"] = missing,
        }));

        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missing}*");
    }

    [Theory]
    [InlineData("bytes=0-9", 100, true, 0, 9)]
    [InlineData("bytes=90-", 100, true, 90, 99)]
    [InlineData("bytes=-10", 100, true, 90, 99)]
    [InlineData("bytes=50-500", 100, true, 50, 99)]
    [InlineData("bytes=100-", 100, false, 0, 0)]
    [InlineData("bytes=9-3", 100, false, 0, 0)]
    [InlineData("bytes=-", 100, false, 0, 0)]
    [InlineData("bytes=0-1,5-9", 100, false, 0, 0)]
    [InlineData("items=0-9", 100, false, 0, 0)]
    [InlineData("bytes=99999999999999999999-", 100, false, 0, 0)]
    public void A_range_header_is_read_or_ignored(string header, long length, bool ok, long start, long end)
    {
        MediaFiles.TryParseRange(header, length, out var s, out var e).Should().Be(ok);
        if (ok)
        {
            (s, e).Should().Be((start, end));
        }
    }

    [Fact]
    public void A_cast_ticket_opens_its_file_on_its_own_site_only()
    {
        var tickets = new CastTickets(new EphemeralDataProtectionProvider());

        var ticket = tickets.Issue(Site, "ZmlsZXMvMTA");

        tickets.Read(Site, ticket).Should().Be("ZmlsZXMvMTA");
        tickets.Read(new DidinSite(8, "/didin"), ticket).Should().BeNull();
        tickets.Read(Site, ticket + "x").Should().BeNull();
        tickets.Read(Site, null).Should().BeNull();
    }

    [Fact]
    public void A_phone_link_is_approved_on_its_site_and_signs_in_once_with_the_secret()
    {
        var links = new DeviceLinks(TimeProvider.System);
        var (entry, secret) = links.Start(Site.SiteId, "Mozilla/5.0 (SMART-TV; Linux; Tizen 5.0) Chrome/63.0", "203.0.113.5")!.Value;

        entry.Device.Should().Be("Chrome on a Samsung TV");
        links.Find(8, entry.Code).Should().BeNull();
        links.Decide(8, entry.Code, true, 1, "someone").Should().BeFalse();
        links.Status(Site.SiteId, entry.Code, secret).Should().Be("pending");

        links.Decide(Site.SiteId, DeviceLinks.Format(entry.Code).ToLowerInvariant(), true, 1, "someone").Should().BeTrue();

        links.Redeem(Site.SiteId, entry.Code, "not-the-secret").Should().BeNull();
        links.Redeem(Site.SiteId, entry.Code, secret)!.UserId.Should().Be(1);
        links.Redeem(Site.SiteId, entry.Code, secret).Should().BeNull();
    }

    [Theory]
    [InlineData("""{"reply":"Try these.","picks":[{"id":3,"reason":"calm"},{"id":"4"},{"id":0},{"x":1}]}""", "Try these.", new[] { 3, 4 })]
    [InlineData("Sure! ```json\n{\"reply\":\"Here.\",\"picks\":[{\"id\":2}]}\n```", "Here.", new[] { 2 })]
    [InlineData("no json at all", null, new int[0])]
    [InlineData("{not json}", null, new int[0])]
    public void The_advisor_reads_the_model_answer_and_keeps_only_usable_ids(string text, string? reply, int[] ids)
    {
        var (said, picks) = AdvisorController.Parse(text);

        said.Should().Be(reply);
        picks.Select(p => p.TitleId).Should().Equal(ids);
    }

    private readonly CastTickets _tickets = new(new EphemeralDataProtectionProvider());

    private async Task<(StreamController Controller, DefaultHttpContext Http, string Token)> StreamSetup(Dictionary<string, string?> settings)
    {
        File.WriteAllBytes(Path.Combine(Root, "media", "one.mp4"), Enumerable.Range(0, 100).Select(i => (byte)i).ToArray());
        var import = new CatalogImport(_store, new DidinFiles(Path.Combine(_folder, "cache")), TimeProvider.System);
        (await import.ImportAsync(Site, new CatalogImportRequest
        {
            Version = "media",
            Titles = [new TitleDoc { Id = "titles/1", Kind = "movie", Title = "The Lantern Keeper" }],
            Files = [new FileDoc { Id = "files/10", TitleId = 1, FilePath = "media/one.mp4", FileName = "one.mp4", Status = "ok" }],
        })).Ok.Should().BeTrue();

        var configuration = Settings(settings);
        var query = new CatalogQuery(_store, _cache, configuration);
        var controller = new StreamController(query, new MediaFiles(new MediaSource(configuration), query), _tickets);
        var http = new DefaultHttpContext();
        http.Items[DidinSiteOnlyAttribute.ItemKey] = Site;
        http.Response.Body = new MemoryStream();
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return (controller, http, FileToken.Encode("files/10"));
    }

    [Fact]
    public async Task A_listed_file_streams_from_the_local_root_with_its_range()
    {
        var (controller, http, token) = await StreamSetup(new() { ["MediaStore:Root"] = Root });
        http.Request.Headers.Range = "bytes=10-19";

        await controller.Get(token);

        http.Response.StatusCode.Should().Be(206);
        http.Response.ContentType.Should().Be("video/mp4");
        http.Response.Headers.ContentRange.ToString().Should().Be("bytes 10-19/100");
        ((MemoryStream)http.Response.Body).ToArray().Should().Equal(Enumerable.Range(10, 10).Select(i => (byte)i));
    }

    [Fact]
    public async Task A_download_ticket_gives_the_original_to_keep_and_a_forged_one_nothing()
    {
        var (controller, http, token) = await StreamSetup(new() { ["MediaStore:Root"] = Root });

        await controller.Cast(_tickets.Issue(Site, token), "1");

        http.Response.StatusCode.Should().Be(200);
        http.Response.Headers.ContentDisposition.ToString().Should().StartWith("attachment").And.Contain("one.mp4");
        ((MemoryStream)http.Response.Body).Length.Should().Be(100);

        var (forged, forgedHttp, _) = await StreamSetup(new() { ["MediaStore:Root"] = Root });
        await forged.Cast("not-a-ticket", "1");
        forgedHttp.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task A_download_ticket_is_issued_for_a_listed_file_only()
    {
        var (_, _, token) = await StreamSetup(new() { ["MediaStore:Root"] = Root });
        var cast = new CastController(new CatalogQuery(_store, _cache, Settings(new() { ["MediaStore:Root"] = Root })), _tickets);
        var http = new DefaultHttpContext();
        http.Items[DidinSiteOnlyAttribute.ItemKey] = Site;
        cast.ControllerContext = new ControllerContext { HttpContext = http };

        var issued = (await cast.Download(token)).Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<CastMedia>().Subject;
        issued.Url.Should().Contain("/api/cast/").And.EndWith("/media?download=1");
        _tickets.Read(Site, issued.Url.Split('/')[^2]).Should().Be(token);

        (await cast.Download(FileToken.Encode("files/11"))).Should().BeOfType<NotFoundResult>();
    }

    [Theory]
    [InlineData(null, "https://store.example/media/one.mp4")]
    [InlineData("1", "https://store.example/media/one.mp4?download=1")]
    public async Task A_public_store_is_asked_to_save_a_download(string? download, string location)
    {
        var (controller, http, token) = await StreamSetup(new() { ["MediaStore:PublicBase"] = "https://store.example/" });

        await controller.Get(token, download);

        http.Response.StatusCode.Should().Be(302);
        http.Response.Headers.Location.ToString().Should().Be(location);
    }

    [Fact]
    public async Task Only_a_listed_file_streams()
    {
        var (controller, http, _) = await StreamSetup(new() { ["MediaStore:Root"] = Root });

        await controller.Get(FileToken.Encode("files/11"));

        http.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task Without_a_media_source_streaming_is_unavailable()
    {
        var (controller, http, token) = await StreamSetup(new());

        await controller.Get(token);

        http.Response.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task A_download_names_the_original_file()
    {
        var (controller, http, token) = await StreamSetup(new() { ["MediaStore:Root"] = Root });

        await controller.Get(token, "1");

        http.Response.StatusCode.Should().Be(200);
        http.Response.Headers.ContentDisposition.ToString().Should().Contain("attachment").And.Contain("one.mp4");
        ((MemoryStream)http.Response.Body).Length.Should().Be(100);
    }

    [Fact]
    public async Task A_user_keeps_their_own_list_and_progress()
    {
        var (_, _, token) = await StreamSetup(new() { ["MediaStore:Root"] = Root });
        var query = new CatalogQuery(_store, _cache, Settings(new()));
        MeController For(int userId)
        {
            var http = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "test")),
            };
            http.Items[DidinSiteOnlyAttribute.ItemKey] = Site;
            return new MeController(_store, query, TimeProvider.System) { ControllerContext = new ControllerContext { HttpContext = http } };
        }

        (await For(1).AddToWatchlist(1)).Should().BeOfType<NoContentResult>();
        (await For(1).AddToWatchlist(99)).Should().BeOfType<NotFoundResult>();
        (await For(1).Progress(token, new ProgressUpdate { Position = 40, Duration = 100 })).Should().BeOfType<NoContentResult>();

        var mine = (await For(1).State()).Value!;
        mine.Watchlist.Should().Equal(1);
        mine.Continue.Should().ContainSingle().Which.File.Token.Should().Be(token);
        (await For(2).State()).Value!.Watchlist.Should().BeEmpty();
    }
}
