using System;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// Subtitles for the player, as WebVTT (all a &lt;track&gt; element plays): a sidecar file listed with a video, read
    /// from the same source as the stream, or a track extracted from inside the video ("embedded:{stream}"), from the
    /// module's file cache. Only files the catalogue lists can be read.
    /// </summary>
    [ApiController]
    [Route("api/subs")]
    [Authorize(Policy = DidinBrowsing.Policy)]
    public sealed class SubsController(CatalogQuery query, MediaFiles media, DidinStore store, DidinFiles files, CastTickets tickets) : DidinController
    {
        private const long MaxBytes = 4 * 1024 * 1024;

        // GET {site}/api/cast/{ticket}/sub/{index} - the same subtitle for a cast device: no sign-in (the ticket is the
        // key) and open to any origin.
        [HttpGet("~/api/cast/{ticket}/sub/{index:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> Cast(string ticket, int index)
        {
            Response.Headers.AccessControlAllowOrigin = "*";
            var token = tickets.Read(Site, ticket);
            if (token == null) return StatusCode(403);
            return await Get(token, index);
        }

        // GET {site}/api/subs/{fileToken}/{index}  ->  text/vtt
        [HttpGet("{token}/{index:int}")]
        public async Task<IActionResult> Get(string token, int index)
        {
            var file = await query.GetFileAsync(Site, FileToken.Decode(token), HttpContext.RequestAborted);
            if (file == null || index < 0 || index >= file.Subtitles.Count)
            {
                return NotFound();
            }

            var path = file.Subtitles[index].Path;
            byte[] bytes;
            if (path?.StartsWith("embedded:", StringComparison.Ordinal) == true)
            {
                bytes = await ReadEmbeddedAsync(CatalogQuery.NumberOf(file.Id), path);
            }
            else if (media.Source.IsLocal || media.Source.IsWebDav)
            {
                bytes = await media.ReadSmallAsync(path, MaxBytes, HttpContext.RequestAborted);
            }
            else
            {
                return StatusCode(503);
            }
            if (bytes == null)
            {
                return NotFound();
            }

            Response.Headers.CacheControl = "private, max-age=86400";
            return Content(SubtitleText.ToWebVtt(SubtitleText.Decode(bytes)), "text/vtt; charset=utf-8");
        }

        private async Task<byte[]> ReadEmbeddedAsync(int fileNumber, string path)
        {
            if (!int.TryParse(path["embedded:".Length..], out var stream) || stream < 0)
            {
                return null;
            }
            var doc = await store.LoadAsync<EmbeddedSubtitleDoc>(Site.SiteId, DidinKind.EmbeddedSubtitle, fileNumber, stream, HttpContext.RequestAborted);
            if (doc?.Hash == null) return null;
            await using var content = files.Open(CatalogImport.SubtitlesFolder, doc.Hash);
            if (content == null || content.Length > MaxBytes) return null;
            using var buffer = new System.IO.MemoryStream();
            await content.CopyToAsync(buffer, HttpContext.RequestAborted);
            return buffer.ToArray();
        }
    }
}
