using System;
using System.Linq;
using System.Reflection;
using Didin.Server.Controllers;
using Didin.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Stomme.Infrastructure;

namespace Didin.Server.Startup
{
    /// <summary>
    /// Didin's API addresses answer for the Didin site only, before routing. The host authorizes a request on its
    /// endpoint before any MVC filter runs, so without this an anonymous call to another site's /api/catalog would get
    /// 401 or 403 from [Authorize] and learn that the API exists; <see cref="DidinSiteOnlyAttribute"/> stays as the
    /// second line. The addresses are read from the controllers' own routes.
    /// </summary>
    public sealed class DidinApiGuard : IServerPipelineStartup
    {
        public static readonly string[] Routes = typeof(DidinController).Assembly.GetTypes()
            .Where(t => typeof(DidinController).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetCustomAttributes<RouteAttribute>())
            .Select(r => "/" + r.Template.Trim('/'))
            .Distinct()
            .ToArray();

        public ServerPipelineStage Stage => ServerPipelineStage.BeforeRouting;

        /// <summary>True when the request is for a Didin API address and the site is not Didin's, or Didin is switched off.</summary>
        public static bool Refuses(HttpContext context)
        {
            var path = context.Request.Path;
            return Routes.Any(route => path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase))
                && (context.RequestServices?.GetService(typeof(DidinSwitch)) is DidinSwitch { IsOff: true } || DidinSite.From(context) == null);
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) =>
            app.Use((context, next) =>
            {
                if (Refuses(context))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                return next(context);
            });
    }
}
