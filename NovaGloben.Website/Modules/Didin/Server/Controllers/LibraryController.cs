using System;
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
    /// The library's health for the site's administrators: what the catalogue holds, which files could not be identified
    /// (the review queue, with their notes) and which ones are damaged or missing. An administrator's verdict on a
    /// review-queue file is recorded here; whoever prepares the next import reads the verdicts and carries them into it.
    /// </summary>
    [ApiController]
    [Route("api/admin/library")]
    [Authorize(Roles = RoleNames.Admin)]
    public sealed class LibraryController(DidinStore store, TimeProvider clock) : DidinController
    {
        private static readonly string[] Verdicts = { "ignore", "title", "note" };

        [HttpGet]
        public async Task<LibraryReport> Get()
        {
            var ct = HttpContext.RequestAborted;
            var version = await store.LoadAsync<CatalogVersionDoc>(Site.SiteId, DidinKind.CatalogVersion, 0, ct: ct);
            var files = await store.AllAsync<FileDoc>(Site.SiteId, DidinKind.File, ct);
            var titles = (await store.AllAsync<TitleDoc>(Site.SiteId, DidinKind.Title, ct))
                .ToDictionary(t => CatalogQuery.NumberOf(t.Id), t => t.Year != null ? $"{t.Title} ({t.Year})" : t.Title);
            var verdicts = (await store.AllAsync<ReviewVerdictDoc>(Site.SiteId, DidinKind.ReviewVerdict, ct))
                .GroupBy(v => v.FileId).ToDictionary(g => g.Key, g => g.First());
            string TitleOf(int? id) => id is int i && titles.TryGetValue(i, out var name) ? name : null;
            LibraryFile Describe(FileDoc f) => new() { Path = f.FilePath, Title = TitleOf(f.TitleId), Notes = f.Notes, Token = FileToken.Encode(f.Id) };
            LibraryFile WithVerdict(FileDoc f)
            {
                var item = Describe(f);
                if (verdicts.TryGetValue(f.Id, out var v))
                {
                    item.Verdict = new ReviewVerdict
                    {
                        Verdict = v.Verdict, TitleId = v.TitleId, Title = TitleOf(v.TitleId), Note = v.Note,
                        UserName = v.UserName, UpdatedUtc = v.UpdatedUtc,
                    };
                }
                return item;
            }

            return new LibraryReport
            {
                CatalogVersion = version?.Version,
                SeededUtc = version?.SeededUtc ?? default,
                Titles = titles.Count,
                Files = files.Count,
                Posters = (await store.AllAsync<PosterDoc>(Site.SiteId, DidinKind.Poster, ct)).Count,
                FilesWithSubtitles = files.Count(f => f.Subtitles?.Count > 0),
                Playback = files.GroupBy(f => f.Playback ?? "not probed").ToDictionary(g => g.Key, g => g.Count()),
                Users = (await store.AllAsync<UserStateDoc>(Site.SiteId, DidinKind.UserState, ct)).Count,
                Ratings = (await store.AllAsync<RatingDoc>(Site.SiteId, DidinKind.Rating, ct)).Count,
                ReviewQueue = files.Where(f => f.Status == "unknown").OrderBy(f => f.FilePath).Select(WithVerdict).ToList(),
                // damaged files still in the catalogue; ones set aside are out of it
                Damaged = files.Where(f => f.Playback == "damaged" && f.Status == "indexed").OrderBy(f => f.FilePath).Select(Describe).ToList(),
                // gone from the store: hidden from the catalogue until they return
                Missing = files.Where(f => f.Status == "missing").OrderBy(f => f.FilePath).Select(Describe).ToList(),
            };
        }

        // PUT {site}/api/admin/library/review/{token}  { verdict: ignore | title | note, titleId?, note? }
        [HttpPut("review/{token}")]
        public async Task<IActionResult> SaveVerdict(string token, [FromBody] ReviewVerdictUpdate update)
        {
            var ct = HttpContext.RequestAborted;
            var fileNumber = FileNumber(token);
            if (fileNumber == 0) return BadRequest();
            if (update == null || !Verdicts.Contains(update.Verdict)) return BadRequest("verdict is ignore, title or note");
            var note = string.IsNullOrWhiteSpace(update.Note) ? null : update.Note.Trim();
            if (note?.Length > 500) return BadRequest("a note is at most 500 characters");
            if (update.Verdict == "note" && note == null) return BadRequest("a note verdict needs a note");

            var file = await store.LoadAsync<FileDoc>(Site.SiteId, DidinKind.File, fileNumber, ct: ct);
            if (file == null) return NotFound();
            if (file.Status != "unknown") return Conflict("only files in the review queue take a verdict");
            if (update.Verdict == "title")
            {
                var title = update.TitleId is int id and > 0 ? await store.LoadAsync<TitleDoc>(Site.SiteId, DidinKind.Title, id, ct: ct) : null;
                if (title == null) return BadRequest("titleId is not a catalogue title");
                if (title.Kind == "series") return BadRequest("a series file needs its episode: leave a note instead");
            }

            var verdict = await store.LoadAsync<ReviewVerdictDoc>(Site.SiteId, DidinKind.ReviewVerdict, fileNumber, ct: ct)
                          ?? new ReviewVerdictDoc { Id = $"reviewverdicts/{fileNumber}" };
            verdict.FileId = file.Id;
            verdict.FilePath = file.FilePath;
            verdict.Verdict = update.Verdict;
            verdict.TitleId = update.Verdict == "title" ? update.TitleId : null;
            verdict.Note = note;
            verdict.UserName = User.FindFirst(ClaimTypes.Name)?.Value ?? "administrator";
            verdict.UpdatedUtc = clock.GetUtcNow().UtcDateTime;
            await store.StoreAsync(Site.SiteId, DidinKind.ReviewVerdict, fileNumber, 0, verdict, ct);
            return NoContent();
        }

        // DELETE {site}/api/admin/library/review/{token}  ->  the file goes back to having no verdict
        [HttpDelete("review/{token}")]
        public async Task<IActionResult> DeleteVerdict(string token)
        {
            var fileNumber = FileNumber(token);
            if (fileNumber == 0) return BadRequest();
            await store.DeleteAsync(Site.SiteId, DidinKind.ReviewVerdict, fileNumber, ct: HttpContext.RequestAborted);
            return NoContent();
        }

        private static int FileNumber(string token) =>
            FileToken.Decode(token) is { } id && id.StartsWith("files/", StringComparison.Ordinal) ? CatalogQuery.NumberOf(id) : 0;
    }
}
