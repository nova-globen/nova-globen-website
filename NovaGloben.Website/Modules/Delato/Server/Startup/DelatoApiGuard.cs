using System.Reflection;
using Delato.Server.Controllers;
using Delato.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Stomme.Infrastructure;

namespace Delato.Server.Startup;

/// <summary>
/// Delato's API addresses answer for the Delato site only, and only where Delato is on, before routing. The host authorizes a request on its
/// endpoint before any MVC filter runs, so without this an anonymous call to another site's /api/v1/groups would get
/// 401 from [Authorize] and learn that the API exists; <see cref="DelatoSiteOnlyAttribute"/> stays as the second line.
/// The addresses are read from the controllers' own routes.
/// </summary>
public sealed class DelatoApiGuard : IServerPipelineStartup
{
    public static readonly string[] Routes = typeof(DelatoController).Assembly.GetTypes()
        .Where(t => typeof(DelatoController).IsAssignableFrom(t) && !t.IsAbstract)
        .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
        .Select(r => "/" + r.Template.Trim('/'))
        .Distinct()
        .ToArray();

    public ServerPipelineStage Stage => ServerPipelineStage.BeforeRouting;

    /// <summary>True when the request is for a Delato API address and the site is not Delato's, or Delato is off.</summary>
    public static bool Refuses(HttpContext context)
    {
        var path = context.Request.Path;
        return Routes.Any(route => path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase))
            && (context.RequestServices?.GetService(typeof(DelatoSwitch)) is DelatoSwitch { IsOff: true } || DelatoSite.From(context) == null);
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env) =>
        app.Use((context, next) =>
        {
            if (Refuses(context))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return Task.CompletedTask;
            }
            return next(context);
        });
}
