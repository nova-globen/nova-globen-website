using System;
using System.Collections.Generic;
using Stomme.Models;

namespace Lek
{
    /// <summary>
    /// The games' static files. A Razor class library's wwwroot is served at <c>/_content/{assembly}/</c> on every host
    /// name, before any site is resolved, so a game page cannot tell which site opened it: the frame says, with
    /// <c>?base=/lek</c> (read by games/site.js). Each page is addressed with <c>?v=</c> and a hash of its content, written
    /// by the build into LekAssetHashes.g.cs, so a release is a new address; the scripts a page loads carry their own
    /// <c>?v=</c>, raised by hand when they change.
    /// </summary>
    public static partial class LekAssets
    {
        public const string Root = "/_content/Lek.Client.Stomme/";

        /// <summary>The site's folder with its leading slash ("/lek"), or "" at the host's root.</summary>
        public static string SiteBase(Alias alias) =>
            string.IsNullOrEmpty(alias?.Path) ? "" : "/" + alias.Path.Trim('/');

        /// <summary>A game page's address for a frame: the library path, its content hash, the site and the page's own query.</summary>
        public static string Frame(string page, Alias alias, IEnumerable<string> query = null)
        {
            var parts = new List<string> { "v=" + Hash(page) };
            var site = SiteBase(alias);
            if (site.Length > 0) parts.Add("base=" + Uri.EscapeDataString(site));
            if (query != null) parts.AddRange(query);
            return Root + page + "?" + string.Join("&", parts);
        }

        /// <summary>The first 12 hex digits of a page's SHA-256 ("games/fuse/index.html"); "0" for a page the build did not hash.</summary>
        public static string Hash(string page) =>
            Hashes.TryGetValue(page, out var hash) ? hash[..12].ToLowerInvariant() : "0";
    }
}
