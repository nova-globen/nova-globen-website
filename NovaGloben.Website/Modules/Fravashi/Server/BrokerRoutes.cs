using System.Text.RegularExpressions;

namespace Fravashi.Broker;

/// <summary>Where the broker answers on the host (the domain map): one place for every prefix and path rule.</summary>
public static partial class BrokerRoutes
{
    /// <summary>The broker's own prefix on the host.</summary>
    public const string Root = "/fravashi";

    /// <summary>The broker's API: token mint, revoke, operator calls and the provider-compatible forward.</summary>
    public const string Api = Root + "/api";

    /// <summary>The signed update channel: each file answers a redirect to its release asset.</summary>
    public const string Updates = Root + "/updates";

    /// <summary>The broker's liveness, beside the host's own /healthz.</summary>
    public const string Health = Root + "/healthz";

    /// <summary>The setting naming the releases the update channel redirects to (a GitHub repository's releases page).</summary>
    public const string ReleasesUrlKey = "Fravashi:Updates:ReleasesUrl";

    /// <summary>Fravashi's public releases.</summary>
    public const string DefaultReleasesUrl = "https://github.com/nova-globen/fravashi/releases";

    /// <summary>The version segment meaning the newest release.</summary>
    public const string Latest = "latest";

    /// <summary>
    /// The path the provider sees: the request's path below <see cref="Api"/> (<c>/v1/messages</c>), never the
    /// host's prefix. Null when the path is not under it.
    /// </summary>
    public static string? UpstreamPath(string? requestPath)
    {
        if (requestPath is null || !requestPath.StartsWith(Api + "/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return requestPath[Api.Length..];
    }

    /// <summary>Whether a name may be asked of the update channel: one file name, no path, no traversal.</summary>
    public static bool IsUpdateFileName(string? name) => name is not null && UpdateFileName().IsMatch(name);

    /// <summary>
    /// Whether a version may be asked of the update channel: digits and dots (<c>1.4.0</c>), with an optional
    /// pre-release part (<c>1.4.0-rc.1</c>); no leading <c>v</c>, nothing else.
    /// </summary>
    public static bool IsUpdateVersion(string? version) => version is not null && UpdateVersion().IsMatch(version);

    /// <summary>
    /// The release asset an update address redirects to, or null when it is refused. Three forms, one file each:
    /// <c>updates/&lt;file&gt;</c> and <c>updates/latest/&lt;file&gt;</c> name the newest release's asset (the launcher
    /// asks the first; the web setup the second), <c>updates/&lt;version&gt;/&lt;file&gt;</c> the asset of the release
    /// tagged <c>v&lt;version&gt;</c>. Pass <paramref name="version"/> null for the first form. The client checks a
    /// file's signature against the address it asked, so the manifest and its signature are asked here alike.
    /// The releases address must be https.
    /// </summary>
    public static string? UpdateTarget(string? releasesUrl, string? version, string? name)
    {
        if (!IsUpdateFileName(name))
        {
            return null;
        }
        var root = (string.IsNullOrWhiteSpace(releasesUrl) ? DefaultReleasesUrl : releasesUrl.Trim()).TrimEnd('/');
        if (!root.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        var file = Uri.EscapeDataString(name!);
        if (version is null || string.Equals(version, Latest, StringComparison.Ordinal))
        {
            return root + "/latest/download/" + file;
        }
        return IsUpdateVersion(version) ? root + "/download/v" + version + "/" + file : null;
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex UpdateFileName();

    [GeneratedRegex(@"^[0-9]+(\.[0-9]+)*(-[0-9A-Za-z]+(\.[0-9A-Za-z]+)*)?$")]
    private static partial Regex UpdateVersion();
}
