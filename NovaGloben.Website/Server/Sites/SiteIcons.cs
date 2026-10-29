#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Stomme.Infrastructure;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// The company's mark as the site icon. The files live with the theme (<see cref="Folder"/>); browsers, crawlers and
    /// home screens ask for them at the root addresses, Stomme's head names <c>favicon.ico</c>, and Stomme's own package
    /// ships a default file at that very address. So the root addresses are answered from the theme's folder, at the
    /// host's earliest stage, before the static files: the request's path is pointed at the theme's file and the static
    /// files serve it as they serve any theme file.
    /// </summary>
    public sealed class SiteIcons : IServerPipelineStartup
    {
        public const string Folder = "/Themes/NovaGloben.Website.Client.Themes.NovaGloben/icons";

        private static readonly Dictionary<string, string> Files = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/favicon.ico"] = Folder + "/favicon.ico",
            ["/favicon.svg"] = Folder + "/favicon.svg",
            ["/apple-touch-icon.png"] = Folder + "/apple-touch-icon.png",
            ["/apple-touch-icon-precomposed.png"] = Folder + "/apple-touch-icon.png",
        };

        public ServerPipelineStage Stage => ServerPipelineStage.BeforeStaticFiles;

        /// <summary>The theme file a root icon address names, or null for any other address.</summary>
        public static string? FileFor(PathString path) =>
            path.HasValue && Files.TryGetValue(path.Value!, out var file) ? file : null;

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env) =>
            app.Use((context, next) =>
            {
                if ((HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
                    && FileFor(context.Request.Path) is { } file)
                {
                    context.Request.Path = file;
                }
                return next(context);
            });
    }
}
