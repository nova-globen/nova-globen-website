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
    /// </summary>
    public static class VisitorCookies
    {
        public const string CultureCookie = ".AspNetCore.Culture";

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
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
                return Task.CompletedTask;
            });
            await next();
        }
    }
}
