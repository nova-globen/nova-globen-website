#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Builder;
using Stomme.Extensions;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// The website's own requests: those resolved to one of its sites (the root site and its language folders, from the
    /// site manifests) and not under a module's route. One image also serves module tenants in their own folders (/didin)
    /// and module routes on the root site's host (/fravashi/api, /fravashi/updates); the website's redirects, its page
    /// rewriting and its cookie rules are not theirs.
    /// </summary>
    public static class WebsiteScope
    {
        /// <summary>
        /// Route prefixes a module maps on the root site's host, host-wide (the domain map). The website never answers
        /// under them.
        /// </summary>
        public static readonly IReadOnlyList<string> ModuleRoutePrefixes = new[] { "/fravashi" };

        /// <summary>Whether a request resolved to the site in <paramref name="sitePath"/>, for <paramref name="requestPath"/>, is the website's.</summary>
        public static bool IsWebsiteRequest(string? sitePath, string? requestPath, IReadOnlyCollection<string> websiteFolders)
        {
            var folder = (sitePath ?? "").Trim('/');
            if (!websiteFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
            var path = requestPath ?? "";
            return !ModuleRoutePrefixes.Any(prefix =>
                path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Runs <paramref name="configuration"/>'s middleware for the website's own requests only.</summary>
        public static IApplicationBuilder UseForWebsite(this IApplicationBuilder app, IReadOnlyCollection<string> websiteFolders, Action<IApplicationBuilder> configuration) =>
            app.UseWhen(context => context.GetAlias() is { } alias && IsWebsiteRequest(alias.Path, context.Request.Path.Value, websiteFolders), configuration);
    }
}
