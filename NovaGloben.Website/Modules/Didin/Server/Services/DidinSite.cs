using Microsoft.AspNetCore.Http;
using Stomme.Extensions;

namespace Didin.Server.Services
{
    /// <summary>
    /// The Didin site a request was resolved to: its id, which every read filters by, and its folder, which every address
    /// Didin hands out begins with ("/didin/api/art/12"), so the same code serves the site on every host.
    /// </summary>
    public sealed record DidinSite(int SiteId, string BasePath)
    {
        /// <summary>The folder Didin's site is in on every host (the domain map).</summary>
        public const string Folder = "didin";

        /// <summary>The Didin site of this request, or null when the request was not resolved to it.</summary>
        public static DidinSite From(HttpContext context)
        {
            if (!context.IsSitePath(Folder))
            {
                return null;
            }
            var alias = context.GetAlias();
            return new DidinSite(alias.SiteId, "/" + (alias.Path ?? Folder).Trim('/'));
        }

        /// <summary>An address on this site: <c>Url("/api/art/12")</c> is <c>/didin/api/art/12</c>.</summary>
        public string Url(string rootRelative) => BasePath + rootRelative;
    }
}
