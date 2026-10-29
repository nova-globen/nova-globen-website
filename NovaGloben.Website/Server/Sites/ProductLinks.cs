#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Addresses a product links to, kept stable however the site behind them changes. Each answers a
    /// temporary redirect (302): the product keeps the address, the site may move what it points at. Every product has its
    /// addresses under <c>/products/&lt;product&gt;</c>:
    /// <list type="bullet">
    /// <item><c>issues</c> (Vacha's Help &gt; Report Issue and its installer's support link) → the contact form with the
    /// product's topic chosen; issues go to info@nova-globen.se through the form;</item>
    /// <item><c>licence</c> (and <c>license</c>, the spelling Vacha's builds use) → the licence file in the product's public
    /// repository;</item>
    /// <item><c>privacy</c> → the privacy statement in the product's public repository.</item>
    /// </list>
    /// A product has only the addresses it has something for. Installed copies still call the old addresses under
    /// <c>/vacha</c> (Vacha 1.1.0, 1.1.1) and <c>/tond</c> (Tond 1.0.0, Vacha's old name); they lead straight to the same
    /// targets, never through a second redirect.
    /// </summary>
    public static class ProductLinks
    {
        private sealed record Product(string Name, string? Licence, string? Privacy, params string[] OldPrefixes);

        private static readonly Product[] Products =
        {
            // Stomme is source-available under PolyForm; its public repository has no privacy statement.
            new("stomme", "https://github.com/nova-globen/stomme/blob/main/LICENSE.md", null),
            // Fravashi has no public repository yet: only its issues address.
            new("fravashi", null, null),
            // Vacha is GPL-3.0-or-later; its licence and privacy statement live in its public repository.
            new("vacha", "https://github.com/nova-globen/vacha/blob/main/LICENSE.txt", "https://github.com/nova-globen/vacha/blob/main/PRIVACY.md", "/vacha", "/tond"),
            // Didin publishes no licence or privacy statement yet: only its issues address.
            new("didin", null, null),
        };

        private static readonly Dictionary<string, string> Targets = Build();

        private static Dictionary<string, string> Build()
        {
            var targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var product in Products)
            {
                foreach (var prefix in new[] { "/products/" + product.Name }.Concat(product.OldPrefixes))
                {
                    foreach (var folder in new[] { "", "/sv" })
                    {
                        targets[folder + prefix + "/issues"] = folder + "/contact?topic=" + product.Name;
                        if (product.Licence != null)
                        {
                            targets[folder + prefix + "/licence"] = product.Licence;
                            targets[folder + prefix + "/license"] = product.Licence;
                        }
                        if (product.Privacy != null)
                        {
                            targets[folder + prefix + "/privacy"] = product.Privacy;
                        }
                    }
                }
            }
            return targets;
        }

        /// <summary>Where a product address points, or null when the path is not one.</summary>
        public static string? Target(string path) => Targets.TryGetValue(path.TrimEnd('/'), out var target) ? target : null;

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                var target = Target(context.Request.Path.Value ?? "");
                if (target != null)
                {
                    context.Response.StatusCode = StatusCodes.Status302Found;
                    context.Response.Headers.Location = target;
                    return;
                }
            }
            await next();
        }
    }
}
