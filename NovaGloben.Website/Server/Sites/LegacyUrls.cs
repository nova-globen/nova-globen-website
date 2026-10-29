#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Keeps every address of the old static site working: an old address
    /// whose page moved answers ONE permanent redirect (301) to where it is now - never a chain.
    /// <list type="bullet">
    /// <item><c>…/index.html</c> → the folder it is the index of;</item>
    /// <item><c>/privacy.html</c>, <c>/terms.html</c> (and the same under a language folder) → <c>/privacy</c>, <c>/terms</c>;</item>
    /// <item>a trailing slash (<c>/blog/</c>, <c>/blog/&lt;slug&gt;/</c>) → the same address without it, which is the
    /// canonical form of every page here; the site roots <c>/</c> and <c>/sv/</c> keep theirs.</item>
    /// </list>
    /// Files are not touched: the covers and assets are served at their old paths by the static-file middleware, which
    /// runs before this one.
    /// </summary>
    public static class LegacyUrls
    {
        private static readonly Dictionary<string, string> Renamed = new(StringComparer.OrdinalIgnoreCase)
        {
            ["privacy.html"] = "privacy",
            ["terms.html"] = "terms",
        };

        /// <summary>
        /// Pages that moved on this site: the service pages folded into others when the six services became four, and the product pages when they moved under /products. A link to one lands where it lives now,
        /// under a language folder too.
        /// </summary>
        private static readonly Dictionary<string, string> Moved = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/services/net-development"] = "/services/architecture",
            ["/services/azure-cloud"] = "/services/architecture",
            ["/services/technical-consulting"] = "/services/architecture",
            ["/services/ai-rag"] = "/services/ai",
            // The product pages moved under /products when the products took their present names:
            // /core was Stomme's page, /deck Fravashi's, and /tond Vacha's under its earlier name. /core, /deck, /vacha and
            // /tond are reserved root-site redirect prefixes. A product's own addresses (issues, licence, privacy, releases.json) under the old
            // prefixes are answered before this (ProductLinks, ProductManifests): installed copies still use them.
            ["/core"] = "/products/stomme",
            ["/deck"] = "/products/fravashi",
            ["/vacha"] = "/products/vacha",
            ["/vacha/download"] = "/products/vacha/download",
            ["/vacha/pricing"] = "/products/vacha/pricing",
            ["/tond"] = "/products/vacha",
            ["/tond/download"] = "/products/vacha/download",
            ["/tond/pricing"] = "/products/vacha/pricing",
        };

        /// <summary>The new address of an old one, or null when the address is current.</summary>
        public static string? Target(string path, IReadOnlyCollection<string> folders)
        {
            if (string.IsNullOrEmpty(path) || path == "/")
            {
                return null;
            }

            var trimmed = path.TrimEnd('/');
            foreach (var folder in folders.Prepend(""))
            {
                var prefix = folder.Length == 0 ? "" : "/" + folder;
                if (trimmed.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase)
                    && Moved.TryGetValue(trimmed[prefix.Length..], out var moved))
                {
                    return prefix + moved;
                }
            }

            var target = path;
            if (target.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase))
            {
                target = target[..^"index.html".Length];
            }

            var lastSlash = target.LastIndexOf('/');
            var last = target[(lastSlash + 1)..];
            if (Renamed.TryGetValue(last, out var renamed))
            {
                target = target[..(lastSlash + 1)] + renamed;
            }

            var isFolderRoot = target == "/" || folders.Any(f => target.Equals("/" + f + "/", StringComparison.OrdinalIgnoreCase));
            if (target.EndsWith('/') && !isFolderRoot)
            {
                target = target.TrimEnd('/');
            }
            if (folders.Any(f => target.Equals("/" + f, StringComparison.OrdinalIgnoreCase)))
            {
                target += "/"; // "/sv/index.html" ends at the folder's root, which keeps its slash
            }

            return target == path ? null : target;
        }

        public static async Task InvokeAsync(HttpContext context, Func<Task> next, IReadOnlyCollection<string> folders)
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                var target = Target(context.Request.Path.Value ?? "", folders);
                if (target != null)
                {
                    context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                    context.Response.Headers.Location = target + context.Request.QueryString.Value;
                    return;
                }
            }
            await next();
        }
    }
}
