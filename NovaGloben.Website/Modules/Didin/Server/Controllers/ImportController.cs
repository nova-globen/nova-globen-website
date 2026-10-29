using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Didin.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stomme.Shared;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// Bringing the catalogue in, for the Didin site's administrators: <c>POST {site}/api/import/catalog</c> (JSON),
    /// <c>POST {site}/api/import/posters/{titleId}</c> (the image as the body) and
    /// <c>POST {site}/api/import/subtitles/{fileNumber}/{stream}?lang=</c> (WebVTT as the body). Upsert by number.
    /// </summary>
    [ApiController]
    [Route("api/import")]
    [Authorize(Roles = RoleNames.Admin)]
    public sealed class ImportController(CatalogImport import) : DidinController
    {
        private const long CatalogLimit = 256L * 1024 * 1024;
        private const long FileLimit = 16L * 1024 * 1024;

        [HttpPost("catalog")]
        [RequestSizeLimit(CatalogLimit)]
        public async Task<ActionResult<CatalogImportResult>> Catalog([FromBody] CatalogImportRequest request, CancellationToken ct)
        {
            if (request is null) return BadRequest();
            var result = await import.ImportAsync(Site, request, ct);
            return result.Ok ? Ok(result) : BadRequest(result);
        }

        [HttpPost("posters/{titleId:int}")]
        [RequestSizeLimit(FileLimit)]
        public async Task<IActionResult> Poster(int titleId, CancellationToken ct)
        {
            if (titleId <= 0 || Request.ContentType?.StartsWith("image/", System.StringComparison.OrdinalIgnoreCase) != true) return BadRequest();
            var image = await ReadAsync(ct);
            if (image.Length == 0) return BadRequest();
            return Ok(new { hash = await import.ImportPosterAsync(Site, titleId, image, Request.ContentType, ct) });
        }

        [HttpPost("subtitles/{fileNumber:int}/{stream:int}")]
        [RequestSizeLimit(FileLimit)]
        public async Task<IActionResult> Subtitle(int fileNumber, int stream, [FromQuery] string lang, CancellationToken ct)
        {
            if (fileNumber <= 0 || stream < 0) return BadRequest();
            var vtt = await ReadAsync(ct);
            if (vtt.Length == 0) return BadRequest();
            return Ok(new { hash = await import.ImportSubtitleAsync(Site, fileNumber, stream, lang, vtt, ct) });
        }

        private async Task<byte[]> ReadAsync(CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            await Request.Body.CopyToAsync(buffer, ct);
            return buffer.ToArray();
        }
    }
}
