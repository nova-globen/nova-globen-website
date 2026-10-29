using Microsoft.AspNetCore.Http;
using Stomme.Extensions;

namespace Delato.Server.Services;

/// <summary>
/// The Delato site a request was resolved to: its id, which every read filters by, and its folder, which every address
/// Delato hands out begins with ("/delato/api/v1/groups/3"), so the same code serves the site on every host.
/// </summary>
public sealed record DelatoSite(int SiteId, string BasePath)
{
    /// <summary>The folder Delato's site is in on every host (the domain map).</summary>
    public const string Folder = "delato";

    /// <summary>The Delato site of this request, or null when the request was not resolved to it.</summary>
    public static DelatoSite? From(HttpContext context)
    {
        if (!context.IsSitePath(Folder))
        {
            return null;
        }
        var alias = context.GetAlias();
        return new DelatoSite(alias.SiteId, "/" + (alias.Path ?? Folder).Trim('/'));
    }

    /// <summary>An address on this site: <c>Url("/api/v1/groups/3")</c> is <c>/delato/api/v1/groups/3</c>.</summary>
    public string Url(string rootRelative) => BasePath + rootRelative;
}
