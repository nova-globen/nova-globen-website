#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// The security headers of every response, and a Content-Security-Policy on the pages an anonymous visitor receives that
    /// allows exactly what those pages load. Stomme already sends X-Frame-Options: SAMEORIGIN.
    ///
    /// The only inline script a page carries is Stomme's theme bootstrap in &lt;head&gt; (App.razor), so the policy
    /// allows the executable inline scripts of the HEAD by their SHA-256 hash, computed from the response itself: it
    /// survives a Stomme upgrade that changes the bootstrap. Nothing in the body is hashed - the body is where pages and
    /// articles are rendered, so a script there is refused. Turnstile's origin is allowed only on a page that loads it, and
    /// Cloudflare Web Analytics' two origins only on a page that carries its beacon (<see cref="WebAnalytics"/>, added here).
    /// A signed-in editor's pages and any interactive page (the sign-in page) are not given the policy and keep Stomme's
    /// scripts; the other headers apply to them too. A static page also loses Stomme's framework scripts
    /// (<see cref="FrameworkScripts"/>).
    /// </summary>
    public static class SecurityHeaders
    {
        public const string Turnstile = "https://challenges.cloudflare.com";

        /// <summary>
        /// The setting that keeps a whole host out of search engines: a copy of the site for testing, set where that host
        /// is deployed. On, every response says <c>X-Robots-Tag: noindex, nofollow</c> and robots.txt disallows everything.
        /// </summary>
        public const string NoIndexKey = "Website:NoIndex";

        /// <summary>Whether this host is kept out of search engines (<see cref="NoIndexKey"/>).</summary>
        public static bool IsNoIndex(IConfiguration configuration) =>
            string.Equals(configuration[NoIndexKey], "true", StringComparison.OrdinalIgnoreCase);

        /// <summary>The site's robots.txt: everything may be crawled, or, on a host kept out of search engines, nothing.</summary>
        public static string Robots(bool noIndex) => noIndex ? "User-agent: *\nDisallow: /\n" : "User-agent: *\nAllow: /\n";

        private static readonly Regex HeadPattern = new(@"<head\b[^>]*>(.*?)</head>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex ScriptPattern = new(@"<script\b([^>]*)>(.*?)</script>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex TypePattern = new(@"\btype\s*=\s*[""']?([^""'\s>]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex SrcPattern = new(@"\bsrc\s*=", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            var configuration = context.RequestServices.GetRequiredService<IConfiguration>();
            var noIndex = IsNoIndex(configuration);
            context.Response.OnStarting(() =>
            {
                var headers = context.Response.Headers;
                if (noIndex)
                {
                    headers["X-Robots-Tag"] = "noindex, nofollow";
                }
                headers["X-Content-Type-Options"] = "nosniff";
                headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
                headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), payment=(), usb=(), interest-cohort=()";
                return Task.CompletedTask;
            });

            if (!IsVisitorPage(context))
            {
                await next();
                return;
            }

            // Buffer the page so its policy can name its own head scripts. The original body feature is set aside, so
            // nothing reaches the client - not even a flush - until the policy is on the response.
            var original = context.Features.Get<IHttpResponseBodyFeature>()!;
            using var buffer = new MemoryStream();
            context.Features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(buffer));
            try
            {
                await next();
            }
            finally
            {
                context.Features.Set(original);
            }

            if (buffer.Length == 0)
            {
                // Nothing written: a 404 the status-code page re-executes into, or a HEAD. Leave the response as it is.
                return;
            }

            byte[] body = buffer.ToArray();
            if (context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true)
            {
                var html = Encoding.UTF8.GetString(body);
                // An interactive page (the sign-in page, which an anonymous visitor may open) needs Stomme's scripts and
                // gets no policy, like a signed-in editor's pages. The public pages are all static.
                if (!IsInteractive(html))
                {
                    html = FrameworkScripts.Remove(html);
                    var analytics = WebAnalytics.Token(configuration, context.RequestServices.GetRequiredService<IHostEnvironment>());
                    if (analytics != null && context.Response.StatusCode == StatusCodes.Status200OK)
                    {
                        html = WebAnalytics.Add(html, analytics);
                    }
                    context.Response.Headers["Content-Security-Policy"] = Policy(html);
                    body = Encoding.UTF8.GetBytes(html);
                }
            }
            context.Response.ContentLength = body.Length;
            await original.Stream.WriteAsync(body, context.RequestAborted);
        }

        /// <summary>A page an anonymous visitor asks for: a GET or HEAD of a path with no file extension.</summary>
        public static bool IsVisitorPage(HttpContext context) =>
            (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
            && context.User?.Identity?.IsAuthenticated != true
            && !Path.HasExtension(context.Request.Path.Value ?? "")
            && !(context.Request.Path.Value ?? "").StartsWith("/_", StringComparison.Ordinal);

        /// <summary>
        /// Whether the page holds an interactive Blazor component: Blazor marks each one with a <c>&lt;!--Blazor:</c>
        /// comment for blazor.web.js to start. A static page has none.
        /// </summary>
        public static bool IsInteractive(string html) => html.Contains("<!--Blazor:", StringComparison.Ordinal);

        /// <summary>The policy for one page's HTML.</summary>
        public static string Policy(string html)
        {
            var scripts = new List<string> { "'self'" };
            scripts.AddRange(HeadScriptHashes(html).Select(h => $"'sha256-{h}'"));
            var frames = new List<string>();
            var connects = new List<string> { "'self'" };
            if (html.Contains(Turnstile + "/", StringComparison.Ordinal))
            {
                scripts.Add(Turnstile);
                frames.Add(Turnstile);
            }
            if (html.Contains(WebAnalytics.ScriptOrigin + "/", StringComparison.Ordinal))
            {
                scripts.Add(WebAnalytics.ScriptOrigin);
                connects.Add(WebAnalytics.ReportOrigin);
            }

            var directives = new List<string>
            {
                "default-src 'self'",
                "script-src " + string.Join(' ', scripts),
                "style-src 'self' 'unsafe-inline'",
                "img-src 'self' data:",
                "font-src 'self'",
                "connect-src " + string.Join(' ', connects),
                "frame-src " + (frames.Count > 0 ? string.Join(' ', frames) : "'none'"),
                "object-src 'none'",
                "base-uri 'self'",
                "form-action 'self'",
                "frame-ancestors 'self'"
            };
            return string.Join("; ", directives);
        }

        /// <summary>The base64 SHA-256 of each executable inline script in the page's head, in order.</summary>
        public static IEnumerable<string> HeadScriptHashes(string html)
        {
            var head = HeadPattern.Match(html);
            if (!head.Success)
            {
                yield break;
            }
            foreach (Match script in ScriptPattern.Matches(head.Groups[1].Value))
            {
                var attributes = script.Groups[1].Value;
                if (SrcPattern.IsMatch(attributes) || !IsExecutable(attributes))
                {
                    continue;
                }
                yield return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script.Groups[2].Value)));
            }
        }

        private static bool IsExecutable(string attributes)
        {
            var type = TypePattern.Match(attributes);
            if (!type.Success)
            {
                return true;
            }
            var value = type.Groups[1].Value.ToLowerInvariant();
            return value is "module" or "text/javascript" or "application/javascript";
        }
    }
}
