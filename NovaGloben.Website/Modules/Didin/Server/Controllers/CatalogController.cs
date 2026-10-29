using System.Threading.Tasks;
using Didin.Server.Services;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Didin.Server.Controllers
{
    [ApiController]
    [Route("api/catalog")]
    [Authorize]
    public sealed class CatalogController(CatalogQuery query) : DidinController
    {
        /// <summary>The whole slim catalogue (cached client-side by Version).</summary>
        [HttpGet]
        public Task<CatalogDocument> Get() => query.GetCatalogAsync(Site, HttpContext.RequestAborted);

        [HttpGet("version")]
        public async Task<object> Version()
        {
            var (version, titleCount, generatedUtc) = await query.GetVersionAsync(Site, HttpContext.RequestAborted);
            return new { Version = version, TitleCount = titleCount, GeneratedUtc = generatedUtc };
        }
    }

    /// <summary>A person's titles: GET {site}/api/people?name=Ada%20Example.</summary>
    [ApiController]
    [Route("api/people")]
    [Authorize]
    public sealed class PeopleController(CatalogQuery query) : DidinController
    {
        [HttpGet]
        public async Task<ActionResult<PersonTitles>> Get([FromQuery] string name)
        {
            var person = await query.GetPersonAsync(Site, name, HttpContext.RequestAborted);
            return person == null ? NotFound() : person;
        }
    }

    [ApiController]
    [Route("api/titles")]
    [Authorize]
    public sealed class TitlesController(CatalogQuery query) : DidinController
    {
        [HttpGet("{id:int}")]
        public async Task<ActionResult<TitleDetails>> Get(int id)
        {
            var details = await query.GetDetailsAsync(Site, id, HttpContext.RequestAborted);
            return details == null ? NotFound() : details;
        }
    }
}
