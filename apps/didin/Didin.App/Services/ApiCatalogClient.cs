using System.Net.Http.Headers;
using System.Net.Http.Json;
using Didin.Module.Media.Services;
using Didin.Shared.Models;

namespace Didin.App.Services;

/// <summary>
/// The catalogue client of the app: the module's API at the site's address, over plain HttpClient, with the signed-in
/// person's access token in the Authorization header (never in a URL) and without one for a guest. The same calls and
/// addresses as the module's own client on the site.
/// </summary>
public sealed class ApiCatalogClient(HttpClient http, MediaOptions options, ISignIn signIn) : ICatalogClient
{
    private string Api(string path) => $"{options.SiteBase}/api/{path}";

    public Task<CatalogVersion> GetCatalogVersionAsync() => GetAsync<CatalogVersion>(Api("catalog/version"));
    public async Task<CatalogDocument> GetCatalogAsync() => await GetAsync<CatalogDocument>(Api("catalog")) ?? new CatalogDocument();
    public Task<TitleDetails> GetTitleAsync(int id) => GetAsync<TitleDetails>(Api($"titles/{id}"));
    public async Task<UserState> GetUserStateAsync() => await GetAsync<UserState>(Api("me/state")) ?? new UserState();
    public Task SaveProgressAsync(string fileToken, double position, double duration) =>
        SendAsync(HttpMethod.Put, Api($"me/progress/{fileToken}"), JsonContent.Create(new ProgressUpdate { Position = position, Duration = duration }));
    public Task SetWatchlistAsync(int titleId, bool onList) => SendAsync(onList ? HttpMethod.Put : HttpMethod.Delete, Api($"me/watchlist/{titleId}"), null);
    public Task SetSeenAsync(int titleId, bool seen) => SendAsync(seen ? HttpMethod.Put : HttpMethod.Delete, Api($"me/seen/{titleId}"), null);
    public Task SkipFinishedAsync(int titleId) => SendAsync(HttpMethod.Put, Api($"me/finished/{titleId}/skip"), null);
    public Task<PersonTitles> GetPersonAsync(string name) => GetAsync<PersonTitles>(Api($"people?name={Uri.EscapeDataString(name ?? "")}"));
    public async Task<AdvisorStatus> GetAdvisorStatusAsync() =>
        await GetAsync<AdvisorStatus>(Api("advisor/status")) ?? new AdvisorStatus { Reason = "The assistant is not available." };
    public async Task<AdvisorReply> AskAdvisorAsync(AdvisorRequest request) =>
        await PostAsync<AdvisorReply>(Api("advisor/suggest"), JsonContent.Create(request))
        ?? new AdvisorReply { Error = "The assistant could not answer just now. Try again in a moment." };
    public async Task<TitleRatings> GetRatingsAsync(int titleId) => await GetAsync<TitleRatings>(Api($"me/ratings/{titleId}")) ?? new TitleRatings();
    public Task SaveRatingAsync(int titleId, int stars, string review) =>
        SendAsync(HttpMethod.Put, Api($"me/ratings/{titleId}"), JsonContent.Create(new RatingUpdate { Stars = stars, Review = review }));
    public Task DeleteRatingAsync(int titleId) => SendAsync(HttpMethod.Delete, Api($"me/ratings/{titleId}"), null);
    public Task<LibraryReport> GetLibraryReportAsync() => GetAsync<LibraryReport>(Api("admin/library"));
    public Task SaveReviewVerdictAsync(string fileToken, ReviewVerdictUpdate verdict) =>
        SendAsync(HttpMethod.Put, Api($"admin/library/review/{fileToken}"), JsonContent.Create(verdict));
    public Task DeleteReviewVerdictAsync(string fileToken) => SendAsync(HttpMethod.Delete, Api($"admin/library/review/{fileToken}"), null);
    public Task SetSubtitleLanguageAsync(string lang) => SendAsync(HttpMethod.Put, Api("me/subtitles"), JsonContent.Create(new SubtitlePreference { Lang = lang }));
    public Task<CastMedia> GetCastAsync(string fileToken) => PostAsync<CastMedia>(Api($"cast/{fileToken}"), JsonContent.Create(new { }));
    public Task<CastMedia> GetDownloadAsync(string fileToken) => PostAsync<CastMedia>(Api($"cast/{fileToken}/download"), JsonContent.Create(new { }));

    /// <summary>A text resource the API addressed (a subtitle track); a site-relative address is made absolute.</summary>
    public async Task<string> GetTextAsync(string url)
    {
        using var response = await http.SendAsync(await RequestAsync(HttpMethod.Get, options.AssetUrl(url)));
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : null;
    }

    private async Task<HttpRequestMessage> RequestAsync(HttpMethod method, string url, HttpContent content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        var token = await signIn.GetAccessTokenAsync();
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
        return request;
    }

    private async Task<T> GetAsync<T>(string url)
    {
        using var response = await http.SendAsync(await RequestAsync(HttpMethod.Get, url));
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : default;
    }

    private async Task<T> PostAsync<T>(string url, HttpContent content)
    {
        using var response = await http.SendAsync(await RequestAsync(HttpMethod.Post, url, content));
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>() : default;
    }

    private async Task SendAsync(HttpMethod method, string url, HttpContent content)
    {
        using var response = await http.SendAsync(await RequestAsync(method, url, content));
        response.EnsureSuccessStatusCode();
    }
}
