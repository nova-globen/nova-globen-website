using Delato.Server.Services;
using Delato.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stomme.Shared;

namespace Delato.Server.Controllers;

/// <summary>
/// <c>POST {site}/api/v1/import</c>: brings a whole earlier ledger in, for the Delato site's Host or Administrators only
/// (<see cref="DelatoImport"/>). 200 with what it did, 400 naming the first bad row (nothing written), 409 when an id
/// is already used by the site's own records (nothing written).
/// </summary>
[Route("api/v1/import")]
[Authorize(Roles = RoleNames.Host + "," + RoleNames.Admin)]
public sealed class ImportController(DelatoImport import) : DelatoController
{
    internal const long Limit = 16L * 1024 * 1024;

    [HttpPost]
    [RequestSizeLimit(Limit)]
    public async Task<IActionResult> Import([FromBody] ImportRequest? request) =>
        Answer(await import.ImportAsync(Site.SiteId, request, HttpContext.RequestAborted));
}
