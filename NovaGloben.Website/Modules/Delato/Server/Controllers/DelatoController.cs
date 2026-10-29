using Delato.Server.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Delato.Server.Controllers;

/// <summary>
/// Refuses, with 404, every request not resolved to the Delato site, and every request at all on a host where Delato is
/// off (<see cref="DelatoSwitch"/>). The host maps a module's controllers for every site of the installation and strips
/// a folder site's folder from "api/..." addresses, so without this a request to another site's /api/v1/groups would
/// reach Delato. A resource filter: it runs before anything is bound or read.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DelatoSiteOnlyAttribute : Attribute, IResourceFilter
{
    internal const string ItemKey = "Delato.Site";

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var off = context.HttpContext.RequestServices?.GetService(typeof(DelatoSwitch)) is DelatoSwitch { IsOff: true };
        if (!off && DelatoSite.From(context.HttpContext) is { } site)
        {
            context.HttpContext.Items[ItemKey] = site;
        }
        else
        {
            context.Result = new NotFoundResult();
        }
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }
}

/// <summary>
/// The base of every Delato controller: it answers on the Delato site only, knows which site that is, resolves the
/// signed-in caller and turns the ledger's <see cref="Outcome"/> into the HTTP answer.
/// </summary>
[DelatoSiteOnly]
[ApiController]
public abstract class DelatoController : ControllerBase
{
    /// <summary>The Delato site this request was resolved to (set by <see cref="DelatoSiteOnlyAttribute"/>).</summary>
    protected DelatoSite Site => (DelatoSite)HttpContext.Items[DelatoSiteOnlyAttribute.ItemKey]!;

    /// <summary>Runs a ledger call for the signed-in caller; 401 with the error envelope when there is none.</summary>
    protected async Task<IActionResult> AsCaller(Func<DelatoCaller, Task<Outcome>> call)
    {
        var callers = HttpContext.RequestServices.GetRequiredService<IDelatoCallers>();
        var caller = await callers.CurrentAsync(User, Site.SiteId);
        return Answer(caller is null ? Outcome.Unauthorized("Missing or invalid sign-in.") : await call(caller));
    }

    protected IActionResult Answer(Outcome outcome) =>
        outcome.Status == 204 ? NoContent() : StatusCode(outcome.Status, outcome.Body);
}
