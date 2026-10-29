using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stomme.Shared;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// The signed-in user's own state on this site: their list of titles, what they have seen, where they stopped in each
    /// file, their subtitle language and their ratings. The user is whoever the request authenticates as (cookie or
    /// JWT), so a user can only ever read and change their own state.
    /// </summary>
    [ApiController]
    [Route("api/me")]
    [Authorize]
    public sealed class MeController(DidinStore store, CatalogQuery query, TimeProvider clock) : DidinController
    {
        private const int ContinueCount = 20;
        private const int FinishedCount = 12;

        // GET {site}/api/me/state  ->  { watchlist: [titleId], continue: [ ... newest first ] }
        [HttpGet("state")]
        public async Task<ActionResult<UserState>> State()
        {
            if (UserId() is not int userId) return Forbid();
            var state = await LoadAsync(userId);
            var result = new UserState
            {
                Watchlist = state?.Watchlist ?? new(),
                IsAdmin = User.IsInRole(RoleNames.Admin),
                SubtitleLang = state?.SubtitleLang,
                Seen = state?.Seen.Select(m => m.TitleId).ToList() ?? new(),
            };
            if (state == null || state.Progress.Count == 0) return result;

            var snapshot = await query.SnapshotAsync(Site, HttpContext.RequestAborted);
            result.Finished = await FinishedAsync(snapshot, userId, state);

            // part-way only: started, and not (nearly) finished
            var open = state.Progress
                .Where(kv => kv.Value.Position >= 30 && (kv.Value.Duration <= 0 || kv.Value.Position < kv.Value.Duration * 0.95))
                .OrderByDescending(kv => kv.Value.UpdatedUtc)
                .Take(ContinueCount);
            foreach (var (fileId, p) in open)
            {
                if (!snapshot.Files.TryGetValue(CatalogQuery.NumberOf(fileId), out var file) || p.TitleId == null) continue;
                if (!snapshot.Titles.TryGetValue(p.TitleId.Value, out var title)) continue;
                string label = null;
                if (p.EpisodeId != null && snapshot.EpisodesByTitle[p.TitleId.Value].FirstOrDefault(e => CatalogQuery.NumberOf(e.Id) == p.EpisodeId) is { } ep)
                {
                    label = $"S{ep.Season:00}E{ep.Episode:00}{(string.IsNullOrEmpty(ep.Name) ? "" : " - " + ep.Name)}";
                }
                result.Continue.Add(new ContinueItem
                {
                    TitleId = p.TitleId.Value,
                    Title = title.Title,
                    Label = label,
                    File = query.DescribeFile(Site, file),
                    Position = p.Position,
                    Duration = p.Duration,
                    UpdatedUtc = p.UpdatedUtc,
                });
            }
            return result;
        }

        /// <summary>
        /// "Finished watching": titles played to the end - a film's last part (or its only file), a series' last episode -
        /// that the user has not rated, skipped or marked seen yet. Newest first.
        /// </summary>
        private async Task<List<FinishedItem>> FinishedAsync(CatalogSnapshot snapshot, int userId, UserStateDoc state)
        {
            var done = state.Progress
                .Where(kv => kv.Value.TitleId != null && kv.Value.Duration > 0 && kv.Value.Position >= kv.Value.Duration * 0.95)
                .ToList();
            if (done.Count == 0) return new();
            var closed = state.Seen.Select(m => m.TitleId).Concat(state.FinishedSkipped).ToHashSet();
            var rated = (await store.AllAsync<RatingDoc>(Site.SiteId, DidinKind.Rating, HttpContext.RequestAborted))
                .Where(r => r.UserId == userId).Select(r => r.TitleId).ToHashSet();

            var finished = new Dictionary<int, DateTime>();
            foreach (var (fileId, p) in done)
            {
                var titleId = p.TitleId.Value;
                if (closed.Contains(titleId) || rated.Contains(titleId)) continue;
                if (!snapshot.Files.TryGetValue(CatalogQuery.NumberOf(fileId), out var file) || file.Role != null) continue;
                bool ends;
                if (p.EpisodeId == null)
                {
                    ends = file.Part == null || file.PartCount == null || file.Part >= file.PartCount;
                }
                else
                {
                    var last = snapshot.EpisodesByTitle[titleId].OrderByDescending(e => e.Season).ThenByDescending(e => e.Episode).FirstOrDefault();
                    ends = last != null && CatalogQuery.NumberOf(last.Id) == p.EpisodeId;
                }
                if (!ends) continue;
                if (!finished.TryGetValue(titleId, out var when) || p.UpdatedUtc > when) finished[titleId] = p.UpdatedUtc;
            }
            return finished.OrderByDescending(kv => kv.Value).Take(FinishedCount)
                .Select(kv => new FinishedItem { TitleId = kv.Key, FinishedUtc = kv.Value }).ToList();
        }

        // PUT / DELETE {site}/api/me/seen/{titleId} - seen titles leave every list but search and Watched
        [HttpPut("seen/{titleId:int}")]
        public Task<IActionResult> MarkSeen(int titleId) => SetSeen(titleId, true);

        [HttpDelete("seen/{titleId:int}")]
        public Task<IActionResult> UnmarkSeen(int titleId) => SetSeen(titleId, false);

        private async Task<IActionResult> SetSeen(int titleId, bool seen)
        {
            if (UserId() is not int userId) return Forbid();
            if (seen && !await TitleExistsAsync(titleId)) return NotFound();
            var state = await LoadAsync(userId) ?? New(userId);
            state.Seen.RemoveAll(m => m.TitleId == titleId);
            if (seen) state.Seen.Insert(0, new SeenMark { TitleId = titleId, SeenUtc = Now });
            await SaveAsync(state);
            return NoContent();
        }

        // PUT {site}/api/me/finished/{titleId}/skip - "Skip" on Finished watching: not rated, not seen, just off that row
        [HttpPut("finished/{titleId:int}/skip")]
        public async Task<IActionResult> SkipFinished(int titleId)
        {
            if (UserId() is not int userId) return Forbid();
            var state = await LoadAsync(userId) ?? New(userId);
            if (!state.FinishedSkipped.Contains(titleId)) state.FinishedSkipped.Add(titleId);
            await SaveAsync(state);
            return NoContent();
        }

        // PUT {site}/api/me/progress/{fileToken}  { position, duration }
        [HttpPut("progress/{token}")]
        public async Task<IActionResult> Progress(string token, [FromBody] ProgressUpdate update)
        {
            if (UserId() is not int userId) return Forbid();
            if (update == null || update.Position < 0 || double.IsNaN(update.Position) || double.IsNaN(update.Duration)) return BadRequest();
            var fileId = FileToken.Decode(token);
            if (fileId == null) return BadRequest();

            var file = await query.GetFileAsync(Site, fileId, HttpContext.RequestAborted);
            if (file == null) return NotFound();
            var state = await LoadAsync(userId) ?? New(userId);
            state.Progress[file.Id] = new ProgressEntry
            {
                TitleId = file.TitleId,
                EpisodeId = file.EpisodeId,
                Position = update.Position,
                Duration = update.Duration,
                UpdatedUtc = Now,
            };
            // watched again from the start: a skip on "Finished watching" was for the last time through
            if (file.TitleId is int titleId && update.Duration > 0 && update.Position < update.Duration * 0.5)
            {
                state.FinishedSkipped.Remove(titleId);
            }
            await SaveAsync(state);
            return NoContent();
        }

        // GET {site}/api/me/ratings/{titleId}  ->  the title's average, count, this user's rating and everyone's reviews
        [HttpGet("ratings/{titleId:int}")]
        public async Task<ActionResult<TitleRatings>> Ratings(int titleId)
        {
            if (UserId() is not int userId) return Forbid();
            var all = await store.AllWithNumberAsync<RatingDoc>(Site.SiteId, DidinKind.Rating, titleId, HttpContext.RequestAborted);
            var info = all.Select(r => new RatingInfo { User = r.UserName, Stars = r.Stars, Review = r.Review, UpdatedUtc = r.UpdatedUtc }).ToList();
            return new TitleRatings
            {
                Count = all.Count,
                Average = all.Count > 0 ? Math.Round(all.Average(r => r.Stars), 1) : null,
                Mine = all.Where(r => r.UserId == userId).Select(r => new RatingInfo { User = r.UserName, Stars = r.Stars, Review = r.Review, UpdatedUtc = r.UpdatedUtc }).FirstOrDefault(),
                Reviews = info.Where(r => !string.IsNullOrWhiteSpace(r.Review)).OrderByDescending(r => r.UpdatedUtc).ToList(),
            };
        }

        // PUT {site}/api/me/ratings/{titleId}  { stars: 1-5, review? }
        [HttpPut("ratings/{titleId:int}")]
        public async Task<IActionResult> Rate(int titleId, [FromBody] RatingUpdate update)
        {
            if (UserId() is not int userId) return Forbid();
            if (update == null || update.Stars < 1 || update.Stars > 5 || (update.Review?.Length ?? 0) > 1000) return BadRequest();
            if (!await TitleExistsAsync(titleId)) return NotFound();
            var rating = await store.LoadAsync<RatingDoc>(Site.SiteId, DidinKind.Rating, titleId, userId, HttpContext.RequestAborted)
                         ?? new RatingDoc { Id = $"ratings/{titleId}-{userId}", TitleId = titleId, UserId = userId };
            rating.UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? $"user {userId}";
            rating.Stars = update.Stars;
            rating.Review = string.IsNullOrWhiteSpace(update.Review) ? null : update.Review.Trim();
            rating.UpdatedUtc = Now;
            await store.StoreAsync(Site.SiteId, DidinKind.Rating, titleId, userId, rating, HttpContext.RequestAborted);
            return NoContent();
        }

        [HttpDelete("ratings/{titleId:int}")]
        public async Task<IActionResult> Unrate(int titleId)
        {
            if (UserId() is not int userId) return Forbid();
            await store.DeleteAsync(Site.SiteId, DidinKind.Rating, titleId, userId, HttpContext.RequestAborted);
            return NoContent();
        }

        // PUT {site}/api/me/subtitles  { lang: "fa" | "en" | ... | "off" }  ->  the language the player turns on by itself
        [HttpPut("subtitles")]
        public async Task<IActionResult> Subtitles([FromBody] SubtitlePreference preference)
        {
            if (UserId() is not int userId) return Forbid();
            var lang = preference?.Lang?.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(lang) || lang.Length > 8 || !lang.All(char.IsAsciiLetterLower)) return BadRequest();
            var state = await LoadAsync(userId) ?? New(userId);
            state.SubtitleLang = lang;
            await SaveAsync(state);
            return NoContent();
        }

        // PUT / DELETE {site}/api/me/watchlist/{titleId}
        [HttpPut("watchlist/{titleId:int}")]
        public Task<IActionResult> AddToWatchlist(int titleId) => SetWatchlist(titleId, true);

        [HttpDelete("watchlist/{titleId:int}")]
        public Task<IActionResult> RemoveFromWatchlist(int titleId) => SetWatchlist(titleId, false);

        private async Task<IActionResult> SetWatchlist(int titleId, bool on)
        {
            if (UserId() is not int userId) return Forbid();
            if (on && !await TitleExistsAsync(titleId)) return NotFound();
            var state = await LoadAsync(userId) ?? New(userId);
            state.Watchlist.Remove(titleId);
            if (on) state.Watchlist.Insert(0, titleId);   // newest first
            await SaveAsync(state);
            return NoContent();
        }

        private DateTime Now => clock.GetUtcNow().UtcDateTime;

        private async Task<bool> TitleExistsAsync(int titleId) =>
            (await query.SnapshotAsync(Site, HttpContext.RequestAborted)).Titles.ContainsKey(titleId);

        private Task<UserStateDoc> LoadAsync(int userId) =>
            store.LoadAsync<UserStateDoc>(Site.SiteId, DidinKind.UserState, userId, ct: HttpContext.RequestAborted);

        private static UserStateDoc New(int userId) => new() { Id = $"userstate/{userId}", UserId = userId };

        private Task SaveAsync(UserStateDoc state) =>
            store.StoreAsync(Site.SiteId, DidinKind.UserState, state.UserId, 0, state, HttpContext.RequestAborted);

        private int? UserId() =>
            int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }
}
