namespace Didin.Module.Media.Services
{
    /// <summary>
    /// Where the UI finds the API: the Didin site's own folder on this host ("/didin"), same-origin, signed in by cookie.
    /// Addresses the API itself returns (a cached poster, a subtitle track) already carry the folder.
    /// </summary>
    public class MediaOptions
    {
        /// <summary>The site's folder with its leading slash ("/didin"), or "" for a site at the host's root.</summary>
        public string SiteBase { get; set; } = "";

        /// <summary>
        /// True for a visitor who is not signed in (a public catalogue lets them browse and play): nothing that is an
        /// account's own - lists, ratings, progress, the assistant, casting - is shown or called.
        /// </summary>
        public bool Guest { get; set; }

        /// <summary>Kept empty: the site's own pages call its API on the same origin.</summary>
        public string ApiBase { get; set; } = "";

        public string CatalogUrl => $"{SiteBase}/api/catalog";
        public string TitlesUrl => $"{SiteBase}/api/titles";

        /// <summary>The original file to keep: the browser saves it under its own name.</summary>
        public string DownloadUrl(string fileToken) => $"{SiteBase}/api/stream/{fileToken}?download=1";

        public string StreamUrl(string fileToken) => $"{SiteBase}/api/stream/{fileToken}";

        /// <summary>A page of this site ("/admin/assistant").</summary>
        public string PageUrl(string path) => SiteBase + path;

        /// <summary>An asset address from the API, already under the site's folder; absolute and empty values pass through.</summary>
        public string AssetUrl(string url) => url;
    }
}
