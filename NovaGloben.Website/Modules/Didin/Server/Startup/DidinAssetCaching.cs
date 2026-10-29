using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Stomme.Infrastructure;

namespace Didin.Server.Startup
{
    /// <summary>
    /// Cache-Control for the Didin client's own static files, under <see cref="DidinAssets.Root"/>. They are answered
    /// before any site is resolved, so this keys on that path, which only Didin serves, and not on the site. An address
    /// with its content hash (<c>?v=</c>) never changes content: immutable for a year. Any other address of these files
    /// is revalidated, so an edge or browser never keeps an old copy past a release.
    /// </summary>
    public sealed class DidinAssetCaching : IServerPipelineStartup
    {
        public const string Immutable = "public, max-age=31536000, immutable";
        public const string Revalidate = "no-cache";

        public ServerPipelineStage Stage => ServerPipelineStage.BeforeStaticFiles;

        /// <summary>The header a request for one of Didin's files gets, or null when the request is not for one.</summary>
        public static string For(HttpRequest request) =>
            (request.Path.Value ?? "").StartsWith(DidinAssets.Root, StringComparison.OrdinalIgnoreCase)
                ? (string.IsNullOrEmpty(request.Query["v"]) ? Revalidate : Immutable)
                : null;

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) =>
            app.Use((context, next) =>
            {
                if (For(context.Request) is { } header)
                {
                    context.Response.OnStarting(() =>
                    {
                        if (context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                        {
                            context.Response.Headers.CacheControl = header;
                        }
                        return System.Threading.Tasks.Task.CompletedTask;
                    });
                }
                return next(context);
            });
    }
}
