using Lek.Server.Elementals;
using Lek.Server.Fuse;
using Lek.Server.Noir;
using Lek.Server.RelicRush;
using Lek.Server.Spark;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.SignalR;
using Stomme.Infrastructure;

namespace Lek.Server.Services
{
    /// <summary>
    /// The host maps a module's controllers and hubs for every site (a folder site's api/ and hubs/ reach the same routes
    /// as the root's). Lek answers on the Lek site only: a controller gets 404 elsewhere, and so does every request to a
    /// Lek hub route (negotiate and connect) - see <see cref="LekHubGuard"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class LekSiteOnlyAttribute : Attribute, IResourceFilter
    {
        public void OnResourceExecuting(ResourceExecutingContext context)
        {
            if (IsOff(context.HttpContext) || LekSite.From(context.HttpContext) == null) context.Result = new NotFoundResult();
        }

        public void OnResourceExecuted(ResourceExecutedContext context) { }

        /// <summary>True on a host that switched Lek off: nothing of Lek answers there.</summary>
        internal static bool IsOff(HttpContext context) =>
            context.RequestServices?.GetService(typeof(LekSwitch)) is LekSwitch { IsOff: true };
    }

    /// <summary>Every Lek controller derives from this: the Lek site only.</summary>
    [LekSiteOnly]
    public abstract class LekController : ControllerBase
    {
    }

    /// <summary>
    /// Every Lek hub derives from this, and its route is in <see cref="LekHubGuard.Routes"/>. The guard is not here: a hub
    /// runs on SignalR's copy of the connection's request, which does not carry the site the host resolved for it.
    /// </summary>
    public abstract class LekHub : Hub
    {
    }

    /// <summary>
    /// The Lek hubs' routes answer for the Lek site only. Runs after the host has resolved the site (and taken the site's
    /// folder off a <c>hubs/</c> path) and before routing, so a request for a Lek hub from any other site - its negotiate
    /// and its connection alike - gets 404 and never reaches SignalR.
    /// </summary>
    public sealed class LekHubGuard : IServerPipelineStartup
    {
        public static readonly string[] Routes =
        {
            FuseEndpoints.HubRoute, SparkEndpoints.HubRoute, NoirEndpoints.HubRoute, RelicEndpoints.HubRoute, ElementalsEndpoints.HubRoute,
        };

        public ServerPipelineStage Stage => ServerPipelineStage.BeforeRouting;

        /// <summary>True when the request is for a Lek hub route and the site is not Lek's, or Lek is switched off.</summary>
        public static bool Refuses(HttpContext context)
        {
            var path = context.Request.Path;
            return Routes.Any(route => path.StartsWithSegments(route, StringComparison.OrdinalIgnoreCase))
                && (LekSiteOnlyAttribute.IsOff(context) || LekSite.From(context) == null);
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
}
