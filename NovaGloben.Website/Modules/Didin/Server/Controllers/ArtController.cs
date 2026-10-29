using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// Cached posters, from the module's file cache. The catalogue links them as {site}/api/art/{id}?v={hash}, so an
    /// address never changes content - the browser may keep it for a year. Signed-in users only, like the rest of the
    /// catalogue; an &lt;img&gt; authenticates by cookie, a token client by ?access_token=.
    /// </summary>
    [ApiController]
    [Route("api/art")]
    [Authorize(Policy = DidinBrowsing.Policy)]
    public sealed class ArtController(DidinStore store, DidinFiles files) : DidinController
    {
        [HttpGet("{id:int}")]
        public async Task<IActionResult> Get(int id)
        {
            var poster = await store.LoadAsync<PosterDoc>(Site.SiteId, DidinKind.Poster, id, ct: HttpContext.RequestAborted);
            var etag = $"\"{poster?.Hash}\"";
            if (poster?.Hash != null && Request.Headers.IfNoneMatch.ToString() == etag)
            {
                return StatusCode(304);
            }
            var content = poster?.Hash == null ? null : files.Open(CatalogImport.PostersFolder, poster.Hash);
            if (content == null)
            {
                return NotFound();
            }

            Response.Headers.ETag = etag;
            Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            return File(content, poster.ContentType ?? "image/jpeg");
        }
    }
}
