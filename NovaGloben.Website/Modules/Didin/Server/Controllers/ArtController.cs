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
    /// address never changes content - the browser may keep it for a year. That address is also the key: v is 128 bits
    /// of the image's own SHA-256, which only the catalogue hands out, so a request carrying the poster's current hash is
    /// answered without sign-in. That is what lets the app's &lt;img&gt; load a private catalogue's posters with no token
    /// in the address. Without the hash (or with a wrong one) a poster is for whoever may browse the catalogue.
    /// </summary>
    [ApiController]
    [Route("api/art")]
    [Authorize(Policy = DidinBrowsing.Policy)]
    public sealed class ArtController(DidinStore store, DidinFiles files, IAuthorizationService authorization) : DidinController
    {
        // The key or the browsing policy: the action checks the policy itself, so the catalogue's own address (it carries
        // the key) needs no sign-in, and any other address needs what the class asks for.
        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<IActionResult> Get(int id, [FromQuery] string v = null)
        {
            var poster = await store.LoadAsync<PosterDoc>(Site.SiteId, DidinKind.Poster, id, ct: HttpContext.RequestAborted);
            if (!Keyed(poster?.Hash, v) && !(await authorization.AuthorizeAsync(User, DidinBrowsing.Policy)).Succeeded)
            {
                return Unauthorized();
            }

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

        /// <summary>True when the address carries the poster's current hash: the catalogue's own link to it.</summary>
        internal static bool Keyed(string storedHash, string given) =>
            !string.IsNullOrEmpty(storedHash) && !string.IsNullOrEmpty(given)
            && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Text.Encoding.ASCII.GetBytes(storedHash), System.Text.Encoding.ASCII.GetBytes(given));
    }
}
