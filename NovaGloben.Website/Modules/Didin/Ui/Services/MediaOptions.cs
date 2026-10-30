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

        /// <summary>
        /// Kept empty: the site's own pages call its API on the same origin. The app sets it (to the site's address), and
        /// the player then fetches subtitle tracks through the catalogue client, with the app's token, instead of naming
        /// another origin's address in a &lt;track&gt;, which the browser refuses.
        /// </summary>
        public string ApiBase { get; set; } = "";

        /// <summary>
        /// False on the site, where the cookie goes with the video's request. True in the app: a &lt;video&gt; cannot send
        /// the app's token, so a signed-in person's player plays the address of a sealed ticket for that one file
        /// (the cast route: {site}/api/cast/{ticket}/media), asked for with the token. A guest plays the plain address,
        /// which only a public catalogue answers.
        /// </summary>
        public bool TicketPlayback { get; set; }

        public string CatalogUrl => $"{SiteBase}/api/catalog";
        public string TitlesUrl => $"{SiteBase}/api/titles";

        /// <summary>The original file to keep: the browser saves it under its own name.</summary>
        public string DownloadUrl(string fileToken) => $"{SiteBase}/api/stream/{fileToken}?download=1";

        public string StreamUrl(string fileToken) => $"{SiteBase}/api/stream/{fileToken}";

        /// <summary>A page of this site ("/admin/assistant").</summary>
        public string PageUrl(string path) => SiteBase + path;

        /// <summary>
        /// Empty on the site. In the app, the host the site is on ("https://nova-globen.se"): the API answers asset
        /// addresses relative to its host ("/didin/api/art/12?v=..."), which the app's own page could not resolve.
        /// </summary>
        public string Origin { get; set; } = "";

        /// <summary>
        /// An asset address from the API, already under the site's folder, made loadable from this page: prefixed with
        /// <see cref="Origin"/> when it is host-relative. Absolute and empty values pass through. Never a token in it.
        /// </summary>
        public string AssetUrl(string url) =>
            string.IsNullOrEmpty(Origin) || string.IsNullOrEmpty(url) || !url.StartsWith('/') || url.StartsWith("//", System.StringComparison.Ordinal)
                ? url
                : Origin + url;
    }
}
