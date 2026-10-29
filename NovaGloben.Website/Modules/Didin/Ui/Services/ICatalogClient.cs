using System;
using System.Threading.Tasks;
using Didin.Shared.Models;

namespace Didin.Module.Media.Services
{
    public interface ICatalogClient
    {
        Task<CatalogVersion> GetCatalogVersionAsync();
        Task<CatalogDocument> GetCatalogAsync();
        Task<TitleDetails> GetTitleAsync(int id);

        // the signed-in user's own state
        Task<UserState> GetUserStateAsync();
        Task SaveProgressAsync(string fileToken, double position, double duration);
        Task SetWatchlistAsync(int titleId, bool onList);
        Task SetSeenAsync(int titleId, bool seen);                           // hidden from lists, kept in Watched
        Task SkipFinishedAsync(int titleId);                                 // off "Finished watching", not rated
        Task<PersonTitles> GetPersonAsync(string name);                      // a person's titles; null if not credited
        Task<AdvisorStatus> GetAdvisorStatusAsync();                         // is "Ask Didin" set up for this site?
        Task<AdvisorReply> AskAdvisorAsync(AdvisorRequest request);          // ten titles for what the user asks
        Task<TitleRatings> GetRatingsAsync(int titleId);
        Task SaveRatingAsync(int titleId, int stars, string review);
        Task DeleteRatingAsync(int titleId);
        Task<LibraryReport> GetLibraryReportAsync();                         // administrators
        Task SaveReviewVerdictAsync(string fileToken, ReviewVerdictUpdate verdict);   // administrators
        Task DeleteReviewVerdictAsync(string fileToken);
        Task SetSubtitleLanguageAsync(string lang);                          // "fa", "en", ... or "off"

        /// <summary>A cast ticket for one file: what a Chromecast needs to play it; null when it cannot.</summary>
        Task<CastMedia> GetCastAsync(string fileToken) => Task.FromResult<CastMedia>(null);

        // signing in another device from this one: what asks, and the answer. Null when the code is unknown
        // or expired. The MAUI shell only asks to be signed in, it never approves, so it keeps these defaults.
        Task<LinkInfo> GetLinkAsync(string code) => Task.FromResult<LinkInfo>(null);
        Task<LinkInfo> DecideLinkAsync(string code, bool approve) => Task.FromResult<LinkInfo>(null);

        /// <summary>
        /// A server-relative text asset (a WebVTT subtitle), for hosts whose page is another origin than the server: a
        /// cross-origin &lt;track&gt; is refused, so the MAUI shell fetches the text and hands it to the page.
        /// Same-origin hosts link the URL directly and keep this default.
        /// </summary>
        Task<string> GetTextAsync(string url) => Task.FromResult<string>(null);
    }

    public class CatalogVersion
    {
        public string Version { get; set; }
        public int TitleCount { get; set; }
        public DateTime GeneratedUtc { get; set; }
    }
}
