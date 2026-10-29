using System.Threading.Tasks;
using Stomme.Controllers;
using Stomme.Infrastructure;
using Stomme.Shared;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// <c>api/Articles</c>: read-only, for a page that renders in a browser (the public pages render on the server and do
    /// not call it). It answers only what the public article pages already show, so it asks for no permission.
    /// </summary>
    [Route(ControllerRoutes.ApiRoute)]
    public sealed class ArticlesController(IArticleService articles, ILogManager logger, IHttpContextAccessor accessor)
        : ModuleControllerBase(logger, accessor)
    {
        // GET api/Articles?page=1&pageSize=12&tag=azure
        [HttpGet]
        public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 12, [FromQuery] string tag = "") =>
            Ok(await articles.ListAsync(page, pageSize, tag));

        // GET api/Articles/latest?count=3
        [HttpGet("latest")]
        public async Task<IActionResult> Latest([FromQuery] int count = 3) => Ok(await articles.LatestAsync(count));

        // GET api/Articles/{slug}
        [HttpGet("{slug}")]
        public async Task<IActionResult> Get(string slug) => await articles.GetAsync(slug) is { } article ? Ok(article) : NotFound();
    }
}
