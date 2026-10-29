#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// The update manifests the products read, at <c>/products/&lt;product&gt;/releases.json</c>
    ///. Vacha's app fetches its manifest from its renderer, origin
    /// <c>vscode-file://vscode-app</c>, so the answer carries <c>Access-Control-Allow-Origin: *</c> and <c>application/json</c>.
    /// The file is <c>content/products/&lt;product&gt;/releases.json</c>, compiled in: a release changes it in a commit, and it
    /// must never name a version that is not yet downloadable. Its "url" is the latest installer, which the app's update
    /// prompt fetches directly.
    /// </summary>
    public static class ProductManifests
    {
        private static readonly Dictionary<string, string> Resources = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/products/vacha/releases.json"] = "content/products/vacha/releases.json",
        };

        /// <summary>
        /// The old addresses installed copies still read: Vacha 1.1.0 reads <c>/vacha/releases.json</c>, Tond 1.0.0 (Vacha's
        /// old name) <c>/tond/releases.json</c>; Vacha's installer upgrades Tond in place. They answer one permanent redirect
        /// (301) to the manifest's address. A cross-origin fetch follows a redirect only when the redirect itself allows the
        /// origin, so the 301 carries <c>Access-Control-Allow-Origin: *</c> too.
        /// </summary>
        private static readonly Dictionary<string, string> Moved = new(StringComparer.OrdinalIgnoreCase)
        {
            ["/vacha/releases.json"] = "/products/vacha/releases.json",
            ["/tond/releases.json"] = "/products/vacha/releases.json",
        };

        /// <summary>The manifest's text for an address, or null when the address is not a manifest.</summary>
        public static string? Read(string path)
        {
            if (!Resources.TryGetValue(path, out var name))
            {
                return null;
            }
            using var stream = typeof(ProductManifests).Assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"The manifest {name} is not compiled into the server.");
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        /// <summary>The manifest address an old one moved to, or null when it is not an old manifest address.</summary>
        public static string? MovedTo(string path) => Moved.TryGetValue(path, out var target) ? target : null;

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            {
                var path = context.Request.Path.Value ?? "";
                var moved = MovedTo(path);
                if (moved != null)
                {
                    context.Response.StatusCode = StatusCodes.Status301MovedPermanently;
                    context.Response.Headers.Location = moved;
                    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    return;
                }
                var json = Read(path);
                if (json != null)
                {
                    context.Response.StatusCode = StatusCodes.Status200OK;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
                    // Short, so a new release reaches every app within minutes of the deploy.
                    context.Response.Headers.CacheControl = "public, max-age=300";
                    if (HttpMethods.IsGet(context.Request.Method))
                    {
                        await context.Response.WriteAsync(json, context.RequestAborted);
                    }
                    return;
                }
            }
            await next();
        }
    }
}
