using Didin.Server.Startup;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Stomme.Models;
using Stomme.Shared;

namespace Didin.Tests;

/// <summary>The middleware Didin adds to the host, and that it stays on the Didin site.</summary>
public sealed class PipelineTests
{
    private const string OldTizen = "Mozilla/5.0 (SMART-TV; Linux; Tizen 5.0) AppleWebKit/537.36 (KHTML, like Gecko) SamsungBrowser/2.2 Chrome/63.0.3239.84 TV Safari/537.36";
    private const string NewChrome = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36";

    private static DefaultHttpContext Request(string? alias, string path, string query = "", string? agent = null)
    {
        var http = new DefaultHttpContext();
        if (alias is not null)
        {
            http.Items[Constants.HttpContextAliasKey] = new Alias { Name = alias, SiteId = 7 };
        }
        http.Request.Method = "GET";
        http.Request.Host = new HostString("nova-globen.test");
        http.Request.Path = path;
        http.Request.QueryString = new QueryString(query);
        if (agent != null) http.Request.Headers.UserAgent = agent;
        return http;
    }

    /// <summary>The hook as the host builds it, ending in a handler that records what reached it.</summary>
    private static async Task<HttpContext> Run(DefaultHttpContext http)
    {
        var app = new ApplicationBuilder(new ServiceProviderStub());
        new DidinPipeline().Configure(app, null!);
        app.Run(context =>
        {
            context.Items["reached"] = true;
            return Task.CompletedTask;
        });
        await app.Build()(http);
        return http;
    }

    private sealed class ServiceProviderStub : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public async Task A_query_token_becomes_the_header_and_leaves_the_address_on_the_Didin_site()
    {
        var http = await Run(Request("nova-globen.test/didin", "/api/stream/abc", "?access_token=t0k&download=1"));

        http.Request.Headers.Authorization.ToString().Should().Be("Bearer t0k");
        http.Request.QueryString.Value.Should().Be("?download=1");
        http.Items.Should().ContainKey("reached");
    }

    [Fact]
    public async Task A_header_already_sent_wins_over_a_query_token()
    {
        var request = Request("nova-globen.test/didin", "/api/art/1", "?access_token=other");
        request.Request.Headers.Authorization = "Bearer mine";

        var http = await Run(request);

        http.Request.Headers.Authorization.ToString().Should().Be("Bearer mine");
        http.Request.QueryString.HasValue.Should().BeFalse();
    }

    [Theory]
    [InlineData("nova-globen.test")]
    [InlineData("nova-globen.test/sv")]
    [InlineData(null)]
    public async Task Another_site_keeps_its_query_and_its_old_browsers(string? alias)
    {
        var http = await Run(Request(alias, "/", "?access_token=t0k", OldTizen));

        http.Request.Headers.ContainsKey("Authorization").Should().BeFalse();
        http.Request.QueryString.Value.Should().Be("?access_token=t0k");
        http.Response.StatusCode.Should().Be(200);
        http.Items.Should().ContainKey("reached");
    }

    [Theory]
    [InlineData("/didin")]
    [InlineData("/didin/")]
    [InlineData("/didin/login")]
    [InlineData("/DIDIN/Login/")]
    public async Task An_old_TV_opening_the_home_or_sign_in_page_goes_to_the_TV_pages(string path)
    {
        var http = await Run(Request("nova-globen.test/didin", path, agent: OldTizen));

        http.Response.StatusCode.Should().Be(302);
        http.Response.Headers.Location.ToString().Should().Be("/didin/tv");
    }

    [Theory]
    [InlineData("/didin/title/3", OldTizen)]
    [InlineData("/didin", NewChrome)]
    [InlineData("/didin/login", "Mozilla/5.0 (SMART-TV; Linux; Tizen 6.5) Chrome/85.0.4183.93 TV Safari/537.36")]
    public async Task Any_other_page_or_browser_passes(string path, string agent)
    {
        var http = await Run(Request("nova-globen.test/didin", path, agent: agent));

        http.Items.Should().ContainKey("reached");
    }

    [Fact]
    public async Task An_old_TV_on_plain_http_moves_to_https_first()
    {
        var request = Request("nova-globen.test/didin", "/didin/tv", "?q=x", OldTizen);
        request.Request.Headers["CF-Visitor"] = "{\"scheme\":\"http\"}";

        var http = await Run(request);

        http.Response.StatusCode.Should().Be(302);
        http.Response.Headers.Location.ToString().Should().Be("https://nova-globen.test/didin/tv?q=x");
    }

    [Theory]
    [InlineData(OldTizen, true)]
    [InlineData("Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) Chrome/76.0 TV", true)]
    [InlineData("Mozilla/5.0 (SMART-TV; Linux; Tizen 7.0) Chrome/94.0 TV", false)]
    [InlineData("Mozilla/5.0 (X11; Linux x86_64) Chrome/84.0.4147.89 Safari/537.36", true)]
    [InlineData(NewChrome, false)]
    [InlineData("Mozilla/5.0 (Macintosh) Version/17.0 Safari/605.1.15", false)]
    [InlineData("", false)]
    public void An_old_engine_is_told_by_its_user_agent(string agent, bool old)
    {
        OldTv.OldEngine(agent).Should().Be(old);
    }
}
