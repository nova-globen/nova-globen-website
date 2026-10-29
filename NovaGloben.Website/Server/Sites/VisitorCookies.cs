#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Keeps an anonymous visitor's cookies to what the site needs.
    /// Stomme sets a one-year culture cookie (.AspNetCore.Culture) on every page; on these sites the language is the
    /// address (/ or /sv/), so the cookie does nothing for a visitor and is not sent to one. A signed-in editor still gets
    /// it - Stomme's interactive admin pages use it. Measured 2026-10-23 on 10.69.0: before, three cookies on every page;
    /// after, two (the antiforgery token and Stomme's release marker).
    /// A culture cookie a visitor's browser still sends - set by an earlier site on the same host - would make a page
    /// say it is in that language whatever its address: it is taken off a visitor page's request before the culture is
    /// read, so the site's own culture decides, and the browser is told to forget it.
    /// </summary>
    public static class VisitorCookies
    {
        public const string CultureCookie = ".AspNetCore.Culture";

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            var stale = SecurityHeaders.IsVisitorPage(context) && WithoutCultureCookie(context.Request);
            context.Response.OnStarting(() =>
            {
                if (context.User?.Identity?.IsAuthenticated != true)
                {
                    var cookies = context.Response.Headers.SetCookie;
                    if (cookies.Any(c => c != null && c.StartsWith(CultureCookie + "=", StringComparison.Ordinal)))
                    {
                        context.Response.Headers.SetCookie = new StringValues(
                            cookies.Where(c => c != null && !c.StartsWith(CultureCookie + "=", StringComparison.Ordinal)).ToArray());
                    }
                }
                if (stale)
                {
                    context.Response.Cookies.Delete(CultureCookie, new CookieOptions { Path = "/" });
                }
                return Task.CompletedTask;
            });
            await next();
        }

        /// <summary>
        /// Takes the culture cookie off the request, if it carries one; true when it did. The request's cookie collection
        /// is parsed again from the changed header, so nothing after this sees the cookie.
        /// </summary>
        public static bool WithoutCultureCookie(HttpRequest request)
        {
            if (!request.Cookies.ContainsKey(CultureCookie))
            {
                return false;
            }
            var kept = request.Headers.Cookie.ToString()
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(pair => !pair.StartsWith(CultureCookie + "=", StringComparison.Ordinal));
            request.Headers.Cookie = string.Join("; ", kept);
            return true;
        }
    }
}
