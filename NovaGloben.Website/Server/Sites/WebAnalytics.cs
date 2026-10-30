#nullable enable
using System;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Cloudflare Web Analytics: its beacon on the pages a visitor receives, counting page views without
    /// cookies. On a host with <see cref="TokenKey"/> set, and never on a host kept out of search engines (the test host)
    /// or in Development, so only the public site is counted. The token is public by design: every visitor's browser
    /// sees it. A page that carries the beacon gets its two origins in its Content-Security-Policy
    /// (<see cref="SecurityHeaders.Policy"/>).
    /// </summary>
    public static class WebAnalytics
    {
        public const string TokenKey = "Website:WebAnalyticsToken";

        /// <summary>Where the beacon script comes from (script-src).</summary>
        public const string ScriptOrigin = "https://static.cloudflareinsights.com";

        /// <summary>Where the beacon reports to (connect-src).</summary>
        public const string ReportOrigin = "https://cloudflareinsights.com";

        private static readonly Regex TokenShape = new("^[0-9a-f]{32}$", RegexOptions.Compiled);

        /// <summary>The token to use on this host, or null when the beacon stays off.</summary>
        public static string? Token(IConfiguration configuration, IHostEnvironment environment)
        {
            var token = configuration[TokenKey]?.Trim();
            return string.IsNullOrEmpty(token) || !TokenShape.IsMatch(token) || SecurityHeaders.IsNoIndex(configuration) || environment.IsDevelopment()
                ? null
                : token;
        }

        /// <summary>The beacon element, as Cloudflare gives it for a manually added site.</summary>
        public static string Beacon(string token) =>
            $"<script defer src=\"{ScriptOrigin}/beacon.min.js\" data-cf-beacon='{{\"token\": \"{token}\"}}'></script>";

        /// <summary>The page with the beacon before its closing body tag; a page without one, or with it already, as it was.</summary>
        public static string Add(string html, string token)
        {
            var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            return end < 0 || html.Contains(ScriptOrigin + "/", StringComparison.Ordinal)
                ? html
                : html[..end] + Beacon(token) + html[end..];
        }
    }
}
