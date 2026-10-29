using System.Threading.Tasks;
using Didin.Shared.Models;
using Stomme.Services;
using Stomme.Shared;

namespace Didin.Module.Media.Services
{
    /// <summary>Stomme's ServiceBase attaches the signed-in user's auth header.</summary>
    public class CatalogClient : ServiceBase, ICatalogClient
    {
        public CatalogClient(System.Net.Http.HttpClient http, SiteState siteState) : base(http, siteState) { }

        public async Task<CatalogVersion> GetCatalogVersionAsync() =>
            await GetJsonAsync<CatalogVersion>($"{CreateApiUrl("catalog")}/version");

        public async Task<CatalogDocument> GetCatalogAsync() =>
            await GetJsonAsync<CatalogDocument>($"{CreateApiUrl("catalog")}") ?? new CatalogDocument();

        public async Task<TitleDetails> GetTitleAsync(int id) =>
            await GetJsonAsync<TitleDetails>($"{CreateApiUrl("titles")}/{id}");

        public async Task<UserState> GetUserStateAsync() =>
            await GetJsonAsync<UserState>($"{CreateApiUrl("me")}/state") ?? new UserState();

        public async Task SaveProgressAsync(string fileToken, double position, double duration) =>
            await PutJsonAsync($"{CreateApiUrl("me")}/progress/{fileToken}", new ProgressUpdate { Position = position, Duration = duration });

        public async Task<TitleRatings> GetRatingsAsync(int titleId) =>
            await GetJsonAsync<TitleRatings>($"{CreateApiUrl("me")}/ratings/{titleId}") ?? new TitleRatings();

        public async Task SaveRatingAsync(int titleId, int stars, string review) =>
            await PutJsonAsync($"{CreateApiUrl("me")}/ratings/{titleId}", new RatingUpdate { Stars = stars, Review = review });

        public async Task DeleteRatingAsync(int titleId) =>
            await DeleteAsync($"{CreateApiUrl("me")}/ratings/{titleId}");

        public async Task SetSubtitleLanguageAsync(string lang) =>
            await PutJsonAsync($"{CreateApiUrl("me")}/subtitles", new SubtitlePreference { Lang = lang });

        public async Task<LibraryReport> GetLibraryReportAsync() =>
            await GetJsonAsync<LibraryReport>($"{CreateApiUrl("admin")}/library");

        public async Task SaveReviewVerdictAsync(string fileToken, ReviewVerdictUpdate verdict) =>
            await PutJsonAsync($"{CreateApiUrl("admin")}/library/review/{fileToken}", verdict);

        public async Task DeleteReviewVerdictAsync(string fileToken) =>
            await DeleteAsync($"{CreateApiUrl("admin")}/library/review/{fileToken}");

        public async Task<LinkInfo> GetLinkAsync(string code) =>
            await GetJsonAsync<LinkInfo>($"{CreateApiUrl("link")}/{System.Uri.EscapeDataString(code ?? "")}");

        public async Task<LinkInfo> DecideLinkAsync(string code, bool approve) =>
            await PostJsonAsync<LinkDecision, LinkInfo>($"{CreateApiUrl("link")}/{System.Uri.EscapeDataString(code ?? "")}/decision",
                new LinkDecision { Approve = approve });

        public async Task SetSeenAsync(int titleId, bool seen)
        {
            if (seen) await PutAsync($"{CreateApiUrl("me")}/seen/{titleId}");
            else await DeleteAsync($"{CreateApiUrl("me")}/seen/{titleId}");
        }

        public async Task<PersonTitles> GetPersonAsync(string name) =>
            await GetJsonAsync<PersonTitles>($"{CreateApiUrl("people")}?name={System.Uri.EscapeDataString(name ?? "")}");

        public async Task<AdvisorStatus> GetAdvisorStatusAsync() =>
            await GetJsonAsync<AdvisorStatus>($"{CreateApiUrl("advisor")}/status") ?? new AdvisorStatus { Reason = "The assistant is not available." };

        public async Task<AdvisorReply> AskAdvisorAsync(AdvisorRequest request) =>
            await PostJsonAsync<AdvisorRequest, AdvisorReply>($"{CreateApiUrl("advisor")}/suggest", request)
            ?? new AdvisorReply { Error = "The assistant could not answer just now. Try again in a moment." };

        public async Task<CastMedia> GetCastAsync(string fileToken) =>
            await PostJsonAsync<object, CastMedia>($"{CreateApiUrl("cast")}/{fileToken}", new { });

        public async Task SkipFinishedAsync(int titleId) =>
            await PutAsync($"{CreateApiUrl("me")}/finished/{titleId}/skip");

        public async Task SetWatchlistAsync(int titleId, bool onList)
        {
            if (onList) await PutAsync($"{CreateApiUrl("me")}/watchlist/{titleId}");
            else await DeleteAsync($"{CreateApiUrl("me")}/watchlist/{titleId}");
        }
    }
}
