#nullable enable
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NovaGloben.Website.Contact
{
    /// <summary>
    /// Cloudflare Turnstile's server-side check: the token the widget put in the form is sent to Cloudflare's siteverify
    /// endpoint with the secret, and only a "success" lets the message through. Fails CLOSED: no secret configured, no
    /// token, an error or no answer - the message is refused, never sent unchecked.
    /// </summary>
    public sealed class Turnstile(IHttpClientFactory http, IConfiguration configuration, ILogger<Turnstile> logger)
    {
        public const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        /// <summary>The secret: configuration <c>Contact:TurnstileSecret</c>, or the file <c>Contact:TurnstileSecretFile</c> names.</summary>
        public string? Secret
        {
            get
            {
                var value = configuration["Contact:TurnstileSecret"];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
                var file = configuration["Contact:TurnstileSecretFile"];
                return !string.IsNullOrWhiteSpace(file) && System.IO.File.Exists(file) ? System.IO.File.ReadAllText(file).Trim() : null;
            }
        }

        public async Task<bool> VerifyAsync(string? token, string? remoteIp, CancellationToken ct)
        {
            var secret = Secret;
            if (string.IsNullOrEmpty(secret))
            {
                logger.LogError("Contact form: no Turnstile secret is configured (Contact:TurnstileSecret or Contact:TurnstileSecretFile); messages are refused.");
                return false;
            }
            if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            {
                return false;
            }

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                var fields = new Dictionary<string, string> { ["secret"] = secret, ["response"] = token };
                if (!string.IsNullOrEmpty(remoteIp))
                {
                    fields["remoteip"] = remoteIp;
                }
                using var response = await http.CreateClient(nameof(Turnstile)).PostAsync(VerifyUrl, new FormUrlEncodedContent(fields), timeout.Token);
                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning("Contact form: Turnstile answered HTTP {Status}.", (int)response.StatusCode);
                    return false;
                }
                var answer = await response.Content.ReadFromJsonAsync<Answer>(cancellationToken: timeout.Token);
                if (answer?.Success != true)
                {
                    logger.LogInformation("Contact form: Turnstile refused a token ({Codes}).", string.Join(",", answer?.ErrorCodes ?? Array.Empty<string>()));
                }
                return answer?.Success == true;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
            {
                logger.LogWarning(ex, "Contact form: Turnstile could not be reached.");
                return false;
            }
        }

        private sealed class Answer
        {
            [JsonPropertyName("success")] public bool Success { get; set; }
            [JsonPropertyName("error-codes")] public string[]? ErrorCodes { get; set; }
        }
    }

    /// <summary>
    /// A fixed-window limit on contact-form posts per client address (by default 5 in 10 minutes), kept in memory: the
    /// site runs as one instance, and a restart forgetting the counts costs nothing. The address is the one Cloudflare
    /// reports (CF-Connecting-IP) when the request came through it, else the connection's.
    /// </summary>
    public sealed class ContactRateLimit(IConfiguration configuration, TimeProvider clock)
    {
        private readonly Dictionary<string, (DateTime Start, int Count)> _windows = new(StringComparer.Ordinal);
        private readonly object _gate = new();

        private int Limit => int.TryParse(configuration["Contact:RateLimit:Count"], out var n) && n > 0 ? n : 5;
        private TimeSpan Window => TimeSpan.FromMinutes(int.TryParse(configuration["Contact:RateLimit:Minutes"], out var m) && m > 0 ? m : 10);

        /// <summary>
        /// The site-wide ceiling per hour, whatever the addresses (default 30): the mail relay allows 60 messages an hour per
        /// client and is shared with every other Nova Globen sender, so a burst from many addresses must
        /// not use it up.
        /// </summary>
        private int SiteWidePerHour => int.TryParse(configuration["Contact:RateLimit:SiteWidePerHour"], out var h) && h > 0 ? h : 30;

        private (DateTime Start, int Count) _siteWide;

        /// <summary>Counts the attempt; false when the address has used up its window, or the site its hour.</summary>
        public bool TryAcquire(string key)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            lock (_gate)
            {
                if (now - _siteWide.Start > TimeSpan.FromHours(1))
                {
                    _siteWide = (now, 0);
                }
                if (_siteWide.Count >= SiteWidePerHour)
                {
                    return false;
                }
                _siteWide = (_siteWide.Start, _siteWide.Count + 1);

                if (_windows.Count > 10_000)
                {
                    foreach (var stale in new List<string>(_windows.Keys))
                    {
                        if (now - _windows[stale].Start > Window) _windows.Remove(stale);
                    }
                }
                if (!_windows.TryGetValue(key, out var window) || now - window.Start > Window)
                {
                    _windows[key] = (now, 1);
                    return true;
                }
                if (window.Count >= Limit)
                {
                    return false;
                }
                _windows[key] = (window.Start, window.Count + 1);
                return true;
            }
        }
    }
}
