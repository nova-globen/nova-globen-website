using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Didin.Server.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Stomme.Extensions;
using Stomme.Infrastructure;

namespace Didin.Server.Startup
{
    /// <summary>
    /// Didin's middleware, for requests to the Didin site only (one image serves several sites): after the host has
    /// resolved the site and before its site-JWT middleware and authentication read the Authorization header.
    /// </summary>
    public sealed class DidinPipeline : IServerPipelineStartup
    {
        public ServerPipelineStage Stage => ServerPipelineStage.BeforeJwt;

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            if (app.ApplicationServices.GetService(typeof(DidinSwitch)) is DidinSwitch { IsOff: true })
            {
                return;
            }
            app.UseWhenSitePath(DidinSite.Folder, didin => didin.Use((context, next) =>
            {
                QueryToken.Move(context.Request);
                if (OldTv.Redirect(context) is { } target)
                {
                    context.Response.Redirect(target);
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                if (CompanyPages.Redirect(context) is { } page)
                {
                    context.Response.Redirect(page, permanent: true);
                    return System.Threading.Tasks.Task.CompletedTask;
                }
                return next(context);
            }));
        }
    }

    /// <summary>
    /// A &lt;video&gt;, &lt;track&gt; or &lt;img&gt; cannot send an Authorization header, so a token client puts its JWT in
    /// <c>?access_token=</c>; it goes into the header (validated exactly as one sent there) and out of the URL, so the
    /// request log, the event log and error pages never see it. A header already sent wins.
    /// </summary>
    public static class QueryToken
    {
        public const string Name = "access_token";

        public static void Move(HttpRequest request)
        {
            var token = request.Query[Name].FirstOrDefault();
            if (string.IsNullOrEmpty(token)) return;
            if (!request.Headers.ContainsKey("Authorization"))
            {
                request.Headers.Authorization = "Bearer " + token;
            }
            request.QueryString = QueryString.Create(request.Query
                .Where(q => !string.Equals(q.Key, Name, StringComparison.OrdinalIgnoreCase))
                .SelectMany(q => q.Value.Select(v => new KeyValuePair<string, string>(q.Key, v))));
        }
    }

    /// <summary>
    /// Short addresses on the Didin site whose page lives on the company site answer a permanent redirect there:
    /// <c>{site}/android</c> to the Android app's download page (the downloads, checksums and terms), and
    /// <c>{site}/privacy</c> and <c>{site}/terms</c> to the company's privacy policy and terms, which cover Didin's
    /// accounts. The site template's own privacy and terms pages are boilerplate that is not true here; the site
    /// reconciler deletes them.
    /// </summary>
    public static class CompanyPages
    {
        public static readonly IReadOnlyDictionary<string, string> Targets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["android"] = "/products/didin/android",
            ["privacy"] = "/privacy",
            ["terms"] = "/terms",
        };

        /// <summary>Where a GET or HEAD of a short address goes, or null for any other request.</summary>
        public static string Redirect(HttpContext context)
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return null;
            var site = DidinSite.From(context);
            if (site == null) return null;
            var path = (request.Path.Value ?? "/").TrimEnd('/');
            var prefix = site.BasePath.TrimEnd('/') + "/";
            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return Targets.TryGetValue(path.Substring(prefix.Length), out var target) ? target : null;
        }
    }

    /// <summary>
    /// A browser too old for the Blazor site goes to the TV pages: a Samsung TV engine before Tizen 6.5 (TVs before 2022:
    /// Chromium 85 and older) or any Chrome before 85, opening the site's home or sign-in page. On such an engine .NET 10's
    /// blazor.web.js cannot start, and the site shows only its header. Such a TV on plain http (its browser knows nothing
    /// of HSTS) moves to https first, so it signs in and stays on one scheme: the proxies in front report https for every
    /// visitor, so the sign-in cookie is marked Secure and a TV on http would drop it. Everything else passes untouched.
    /// </summary>
    public static class OldTv
    {
        private static readonly Regex Tizen = new(@"Tizen (\d+)\.(\d+)", RegexOptions.Compiled);
        private static readonly Regex Chrome = new(@"Chrome/(\d+)\.", RegexOptions.Compiled);

        /// <summary>Where an old TV's GET goes instead, or null when the request passes.</summary>
        public static string Redirect(HttpContext context)
        {
            var request = context.Request;
            if (!HttpMethods.IsGet(request.Method) || !OldEngine(request.Headers.UserAgent.ToString())) return null;
            if (OnPlainHttp(request))
            {
                return "https://" + request.Host + request.PathBase + request.Path + request.QueryString;
            }
            var site = DidinSite.From(context);
            if (site == null) return null;
            var path = (request.Path.Value ?? "/").TrimEnd('/');
            var home = site.BasePath.TrimEnd('/');
            return string.Equals(path, home, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(path, home + "/login", StringComparison.OrdinalIgnoreCase)
                ? site.Url("/tv")
                : null;
        }

        /// <summary>The visitor came over plain http: Cloudflare's CF-Visitor ({"scheme":"http"}), else X-Forwarded-Proto.</summary>
        private static bool OnPlainHttp(HttpRequest request)
        {
            var visitor = request.Headers["CF-Visitor"].ToString();
            if (!string.IsNullOrEmpty(visitor)) return visitor.Replace(" ", "").Contains("\"scheme\":\"http\"", StringComparison.OrdinalIgnoreCase);
            return string.Equals(request.Headers["X-Forwarded-Proto"].ToString(), "http", StringComparison.OrdinalIgnoreCase);
        }

        public static bool OldEngine(string userAgent)
        {
            if (string.IsNullOrEmpty(userAgent)) return false;
            var tizen = Tizen.Match(userAgent);
            if (tizen.Success)
            {
                var major = int.Parse(tizen.Groups[1].Value);
                var minor = int.Parse(tizen.Groups[2].Value);
                return major < 6 || (major == 6 && minor < 5);
            }
            var chrome = Chrome.Match(userAgent);
            return chrome.Success && int.TryParse(chrome.Groups[1].Value, out var version) && version < 85;
        }
    }
}
