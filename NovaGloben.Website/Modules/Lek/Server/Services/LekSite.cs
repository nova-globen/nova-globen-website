using Stomme.Extensions;

namespace Lek.Server.Services
{
    /// <summary>The Lek site a request was resolved to: its id and its folder ("/lek"), or none.</summary>
    public sealed record LekSite(int SiteId, string BasePath)
    {
        /// <summary>The folder Lek's site is in (the domain map: the UAT host's /lek only).</summary>
        public const string Folder = "lek";

        public static LekSite? From(HttpContext? context)
        {
            if (context == null || !context.IsSitePath(Folder))
            {
                return null;
            }
            var alias = context.GetAlias();
            return new LekSite(alias.SiteId, "/" + (alias.Path ?? Folder).Trim('/'));
        }
    }
}
