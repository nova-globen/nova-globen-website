using Stomme.Models;

namespace Didin
{
    /// <summary>
    /// The client's static files. A Razor class library's wwwroot is served at <c>/_content/{assembly}/</c> on every host
    /// name, before any site is resolved, so the address is the same for every site. Each file is addressed with
    /// <c>?v=</c> and a hash of its content (written by the build into DidinAssetHashes.g.cs): a new file is a new
    /// address, which the host serves as immutable.
    /// </summary>
    public static partial class DidinAssets
    {
        public const string Root = "/_content/Didin.Client.Stomme/";

        public const string Theme = "Theme.css";
        public const string Module = "Module.css";
        public const string QrLink = "qr-link.js";

        /// <summary>A file's address with its content hash.</summary>
        public static string Url(string file) => Root + file + "?v=" + Hash(file);

        /// <summary>The site's folder with its leading slash ("/didin"), or "" at the host's root.</summary>
        public static string SiteBase(Alias alias) =>
            string.IsNullOrEmpty(alias?.Path) ? "" : "/" + alias.Path.Trim('/');
    }
}
