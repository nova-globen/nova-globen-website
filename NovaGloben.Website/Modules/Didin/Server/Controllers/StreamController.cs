using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Didin.Server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// Media streaming with HTTP Range support (seeking), from the host's <see cref="MediaSource"/>: a local folder or
    /// the WebDAV store, proxied; or, for a public store, a redirect to the file's own address there, which the browser
    /// streams from directly. The token is the file's document id: only files in the catalogue stream.
    /// </summary>
    [ApiController]
    [Route("api/stream")]
    [Authorize(Policy = DidinBrowsing.Policy)]
    public sealed class StreamController(CatalogQuery query, MediaFiles media, CastTickets tickets) : DidinController
    {
        // GET {site}/api/stream/{token}             - for the player
        // GET {site}/api/stream/{token}?download=1  - the original file to keep, under its own name; resumable
        [HttpGet("{token}")]
        public Task Get(string token, [FromQuery(Name = "download")] string downloadFlag = null) =>
            ServeAsync(token, downloadFlag is "1" or "true");

        // GET {site}/api/cast/{ticket}/media - a cast device playing it: no sign-in, the ticket is the key. Open to any
        // origin: the receiver page fetches from its own.
        [HttpGet("~/api/cast/{ticket}/media")]
        [AllowAnonymous]
        public async Task Cast(string ticket)
        {
            Response.Headers.AccessControlAllowOrigin = "*";
            Response.Headers.AccessControlExposeHeaders = "Content-Length, Content-Range, Accept-Ranges";
            var token = tickets.Read(Site, ticket);
            if (token == null) { Response.StatusCode = 403; return; }
            await ServeAsync(token, download: false);
        }

        private async Task ServeAsync(string token, bool download)
        {
            if (!media.Source.IsLocal && !media.Source.IsWebDav && !media.Source.IsPublic) { Response.StatusCode = 503; return; }

            var file = await query.GetFileAsync(Site, FileToken.Decode(token), HttpContext.RequestAborted);
            if (file == null) { Response.StatusCode = 404; return; }

            // a converted copy plays in place of an original the browser cannot decode - same token, same file; a
            // download is always the original: the best quality, and what a player like VLC takes in any container
            var path = download ? file.FilePath : file.PlaybackPath ?? file.FilePath;
            var name = download || file.PlaybackPath == null ? file.FileName : Path.GetFileName(path);
            if (download && !string.IsNullOrEmpty(name))
            {
                Response.Headers.ContentDisposition = new Microsoft.Net.Http.Headers.ContentDispositionHeaderValue("attachment")
                {
                    FileNameStar = name,
                    FileName = new string(name.Select(c => c < 128 && c != '"' ? c : '_').ToArray()),
                }.ToString();
            }
            if (media.Source.IsPublic)
            {
                var url = media.Source.PublicUrl(path);
                if (url == null) { Response.StatusCode = 400; return; }
                Response.Redirect(url);
            }
            else if (media.Source.IsLocal)
            {
                await StreamLocal(path);
            }
            else
            {
                await ProxyWebDav(path, name ?? path);
            }
        }

        private async Task StreamLocal(string filePath)
        {
            var full = media.Source.LocalPath(filePath);
            if (full == null) { Response.StatusCode = 400; return; }
            if (!System.IO.File.Exists(full)) { Response.StatusCode = 404; return; }

            var length = new FileInfo(full).Length;
            long start = 0, end = length - 1;
            if (MediaFiles.TryParseRange(Request.Headers.Range.ToString(), length, out var rStart, out var rEnd))
            {
                start = rStart;
                end = rEnd;
                Response.StatusCode = 206;
                Response.Headers.ContentRange = $"bytes {start}-{end}/{length}";
            }
            else
            {
                Response.StatusCode = 200;
            }
            Response.ContentType = MediaFiles.ContentType(full);
            Response.Headers.AcceptRanges = "bytes";
            Response.ContentLength = end - start + 1;

            await using var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read);
            fs.Seek(start, SeekOrigin.Begin);
            var remaining = end - start + 1;
            var buffer = new byte[81920];
            try
            {
                while (remaining > 0)
                {
                    var read = await fs.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), HttpContext.RequestAborted);
                    if (read == 0) break;
                    await Response.Body.WriteAsync(buffer.AsMemory(0, read), HttpContext.RequestAborted);
                    remaining -= read;
                }
            }
            catch (OperationCanceledException)
            {
                // the client stopped reading: normal when a video seeks
            }
        }

        private async Task ProxyWebDav(string filePath, string fileName)
        {
            using var upstream = await media.GetWebDavAsync(filePath, Request.Headers.Range.ToString(), HttpContext.RequestAborted);
            if (upstream == null) { Response.StatusCode = 503; return; }
            Response.StatusCode = (int)upstream.StatusCode;
            if (!upstream.IsSuccessStatusCode) return;

            var ours = MediaFiles.ContentType(fileName);
            Response.ContentType = ours != "application/octet-stream"
                ? ours
                : upstream.Content.Headers.ContentType?.ToString() ?? ours;
            if (upstream.Content.Headers.ContentLength is long len)
            {
                Response.ContentLength = len;
            }
            Response.Headers.AcceptRanges = "bytes";
            if (upstream.Content.Headers.ContentRange != null)
            {
                Response.Headers.ContentRange = upstream.Content.Headers.ContentRange.ToString();
            }
            try
            {
                await using var stream = await upstream.Content.ReadAsStreamAsync(HttpContext.RequestAborted);
                await stream.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // the client stopped reading
            }
        }
    }
}
