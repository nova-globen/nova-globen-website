using Delato.Server.Services;
using Delato.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stomme.Shared;

namespace Delato.Server.Controllers;

/// <summary>
/// <c>POST {site}/api/v1/forget</c>: a person added to groups without an account asks to be forgotten; the Delato
/// site's Host or Administrators enter their e-mail address (<see cref="DelatoForget"/>). 200 with how many members in
/// how many groups; 400 without an address.
/// </summary>
[Route("api/v1/forget")]
[Authorize(Roles = RoleNames.Host + "," + RoleNames.Admin)]
public sealed class ForgetController(DelatoForget forget) : DelatoController
{
    [HttpPost]
    public async Task<IActionResult> Forget([FromBody] ForgetRequest? request)
    {
        if (DelatoCaller.NormalizeEmail(request?.Email) is null) return BadRequest(new ErrorEnvelope(new ErrorBody("VALIDATION", "An e-mail address is required.")));
        return Ok(await forget.ForgetEmailAsync(Site.SiteId, request!.Email, HttpContext.RequestAborted));
    }
}