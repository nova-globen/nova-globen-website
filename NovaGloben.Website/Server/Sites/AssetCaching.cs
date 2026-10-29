#nullable enable
using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Stomme.Infrastructure;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// A theme file addressed with its fingerprint (<c>/Themes/…?v=…</c>) never changes at that address: a new release
    /// gives it a new <c>v</c>. So the browser and the edge may keep it a year without asking again. Without a
    /// fingerprint the file keeps the host's static-file headers (validated with its ETag). Static files are answered
    /// before the site's own middleware, so this runs at the host's earliest stage and sets the header as the response
    /// starts, after the static-file handler's own headers.
    /// </summary>
    public sealed class AssetCaching : IServerPipelineStartup
    {
        public const string Immutable = "public, max-age=31536000, immutable";

        public ServerPipelineStage Stage => ServerPipelineStage.BeforeStaticFiles;

        /// <summary>Whether a request is for a fingerprinted theme file.</summary>
        public static bool IsFingerprintedThemeAsset(HttpRequest request) =>
            (request.Path.Value ?? "").StartsWith("/Themes/", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrEmpty(request.Query["v"]);

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) =>
            app.Use((context, next) =>
            {
                if (IsFingerprintedThemeAsset(context.Request))
                {
                    context.Response.OnStarting(() =>
                    {
                        if (context.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                        {
                            context.Response.Headers.CacheControl = Immutable;
                        }
                        return System.Threading.Tasks.Task.CompletedTask;
                    });
                }
                return next(context);
            });
    }
}
