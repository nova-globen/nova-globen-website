using System.Threading.Tasks;
using Didin.Server.Services;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// Cast to TV: what a Chromecast / Google TV needs to play one file. The signed-in user asks for a ticket; the device
    /// then fetches {site}/api/cast/{ticket}/media (StreamController) and .../sub/{i} (SubsController) itself. The
    /// addresses are host-relative: the page puts its own public address in front (a server call cannot see the address
    /// the browser used).
    /// </summary>
    [ApiController]
    [Route("api/cast")]
    [Authorize]
    public sealed class CastController(CatalogQuery query, CastTickets tickets) : DidinController
    {
        // POST {site}/api/cast/{fileToken}  ->  { url, contentType, subtitles: [{ url, lang, label }] }
        [HttpPost("{token}")]
        public async Task<IActionResult> Post(string token)
        {
            var file = await query.GetFileAsync(Site, FileToken.Decode(token), HttpContext.RequestAborted);
            if (file == null || file.Playback is "transcode" or "damaged") return NotFound();

            var ticket = tickets.Issue(Site, token);
            var path = file.PlaybackPath ?? file.FilePath;
            return Ok(new CastMedia
            {
                Url = Site.Url($"/api/cast/{ticket}/media"),
                ContentType = ContentTypeForCast(path),
                // the same tracks in the same order as the player's (SubtitleLabels), so the chosen one maps by index
                Subtitles = SubtitleLabels.Describe(file.FilePath, file.Subtitles, i => Site.Url($"/api/cast/{ticket}/sub/{i}"))
                    .ConvertAll(t => new CastSubtitle { Url = t.Url, Lang = t.Lang, Label = t.Label }),
            });
        }

        // POST {site}/api/cast/{fileToken}/download  ->  { url }: the original file under a ticket, for the app, which
        // hands the download to the device's browser; that browser has no sign-in of its own
        [HttpPost("{token}/download")]
        public async Task<IActionResult> Download(string token)
        {
            var file = await query.GetFileAsync(Site, FileToken.Decode(token), HttpContext.RequestAborted);
            if (file == null || file.Playback == "damaged") return NotFound();
            return Ok(new CastMedia { Url = Site.Url($"/api/cast/{tickets.Issue(Site, token)}/media") + "?download=1" });
        }

        /// <summary>
        /// The type the receiver is told. An MKV is announced as WebM (Matroska's own subset): the receiver picks its
        /// player by type and has no entry for Matroska, while its browser engine plays H.264 MKV as Chrome does.
        /// </summary>
        internal static string ContentTypeForCast(string path)
        {
            var type = MediaFiles.ContentType(path);
            return type == "video/x-matroska" ? "video/webm" : type;
        }
    }
}
