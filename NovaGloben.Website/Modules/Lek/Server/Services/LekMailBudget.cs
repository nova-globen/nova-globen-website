using Microsoft.Extensions.Configuration;
using Stomme.Infrastructure;

namespace Lek.Server.Services
{
    /// <summary>
    /// How much mail the Lek site may make the host send, in total. Stomme limits registration and the other account mail
    /// per client address (5 in 5 minutes); that does not bound the total, and the relay's budget is shared with everything
    /// else the host sends (the contact form, the owner's alerts). So the Lek site's mail-sending account requests -
    /// registration (its confirmation), forgotten password, forgotten username, sign-in link - also share one fixed window
    /// an hour and one a day: <c>Lek:Mail:PerHour</c> (10) and <c>Lek:Mail:PerDay</c> (50). Past either, the request is
    /// answered 429 with Retry-After, as Stomme's own limiter answers. Kept in memory: a restart starts the windows again.
    /// </summary>
    public sealed class LekMailBudget
    {
        public const int DefaultPerHour = 10;
        public const int DefaultPerDay = 50;

        private readonly object _gate = new();
        private readonly TimeProvider _clock;
        private DateTime _hour;
        private DateTime _day;
        private int _inHour;
        private int _inDay;

        public LekMailBudget(IConfiguration configuration, TimeProvider clock)
        {
            _clock = clock;
            PerHour = Limit(configuration["Lek:Mail:PerHour"], DefaultPerHour);
            PerDay = Limit(configuration["Lek:Mail:PerDay"], DefaultPerDay);
        }

        public int PerHour { get; }
        public int PerDay { get; }

        /// <summary>Takes one send from the budget; when there is none left, says how long until there is.</summary>
        public bool TryTake(out TimeSpan retryAfter)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var hour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0, DateTimeKind.Utc);
            var day = now.Date;
            lock (_gate)
            {
                if (hour != _hour) { _hour = hour; _inHour = 0; }
                if (day != _day) { _day = day; _inDay = 0; }
                if (_inDay >= PerDay)
                {
                    retryAfter = day.AddDays(1) - now;
                    return false;
                }
                if (_inHour >= PerHour)
                {
                    retryAfter = hour.AddHours(1) - now;
                    return false;
                }
                _inHour++;
                _inDay++;
                retryAfter = TimeSpan.Zero;
                return true;
            }
        }

        /// <summary>A request that makes the host send mail: Stomme's registration and its account-mail endpoints.</summary>
        public static bool Sends(HttpRequest request)
        {
            var path = request.Path;
            if (HttpMethods.IsPost(request.Method))
            {
                return path.Equals("/api/User", StringComparison.OrdinalIgnoreCase)
                    || path.Equals("/api/User/", StringComparison.OrdinalIgnoreCase);
            }
            return HttpMethods.IsGet(request.Method)
                && (path.StartsWithSegments("/api/User/forgotpassword", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWithSegments("/api/User/forgotusername", StringComparison.OrdinalIgnoreCase)
                    || path.StartsWithSegments("/api/User/loginlink", StringComparison.OrdinalIgnoreCase));
        }

        private static int Limit(string? value, int fallback) =>
            int.TryParse(value, out var n) && n > 0 ? n : fallback;
    }

    /// <summary>Spends <see cref="LekMailBudget"/> on the Lek site's mail-sending requests, after the site is known.</summary>
    public sealed class LekMailBudgetGate : IServerPipelineStartup
    {
        public ServerPipelineStage Stage => ServerPipelineStage.BeforeRouting;

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            var budget = app.ApplicationServices.GetRequiredService<LekMailBudget>();
            app.Use(async (context, next) =>
            {
                if (LekSite.From(context) != null && LekMailBudget.Sends(context.Request) && !budget.TryTake(out var retryAfter))
                {
                    context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                    context.Response.Headers.RetryAfter = ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    context.Response.ContentType = "application/json";
                    await context.Response.WriteAsync("{\"error\":\"Too many requests.\"}");
                    return;
                }
                await next(context);
            });
        }
    }
}
