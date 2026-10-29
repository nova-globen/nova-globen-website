using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Didin.Server.Services
{
    /// <summary>
    /// Pending "sign in with your phone" requests, the device-authorization pattern TV apps use. A browser (a TV's, most
    /// often) asks for a code and gets two things: a short code for people (shown, and in the QR link) and a long secret
    /// it keeps. A phone signed in to the same site approves the short code; only the holder of the secret can then turn
    /// the approval into a session, once. Kept in memory: a request lives ten minutes, and a restart only means scanning
    /// again.
    /// </summary>
    public sealed class DeviceLinks(TimeProvider clock)
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
        private const int MaxPending = 500;                                  // no unbounded growth from anonymous starts
        private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";   // no 0/O, 1/I/L

        public sealed class Entry
        {
            public int SiteId { get; init; }
            public string Code { get; init; }
            public byte[] SecretHash { get; init; }
            public string Device { get; init; }
            public string Ip { get; init; }
            public DateTime CreatedUtc { get; init; }
            public string Status { get; set; } = "pending";         // pending | approved | denied | used
            public int UserId { get; set; }
            public string UserName { get; set; }
        }

        private readonly ConcurrentDictionary<string, Entry> _entries = new();

        private DateTime Now => clock.GetUtcNow().UtcDateTime;

        private bool Expired(Entry e) => Now - e.CreatedUtc > Lifetime;

        /// <summary>A new request on a site; null when too many are pending.</summary>
        public (Entry entry, string secret)? Start(int siteId, string userAgent, string ip)
        {
            Sweep();
            if (_entries.Count >= MaxPending) return null;
            var secret = Base64Url(RandomNumberGenerator.GetBytes(32));
            for (var attempt = 0; attempt < 10; attempt++)
            {
                var entry = new Entry
                {
                    SiteId = siteId,
                    Code = NewCode(),
                    SecretHash = Hash(secret),
                    Device = DescribeDevice(userAgent),
                    Ip = ip,
                    CreatedUtc = Now,
                };
                if (_entries.TryAdd(entry.Code, entry)) return (entry, secret);
            }
            return null;
        }

        /// <summary>The site's request behind a typed or scanned code ("abcd2345", "ABCD-2345" ...), or null.</summary>
        public Entry Find(int siteId, string code)
        {
            var key = Normalize(code);
            return key != null && _entries.TryGetValue(key, out var e) && e.SiteId == siteId && !Expired(e) ? e : null;
        }

        /// <summary>The phone's answer. Only a pending request can be decided.</summary>
        public bool Decide(int siteId, string code, bool approve, int userId, string userName)
        {
            var e = Find(siteId, code);
            if (e == null) return false;
            lock (e)
            {
                if (e.Status != "pending") return false;
                e.Status = approve ? "approved" : "denied";
                if (approve)
                {
                    e.UserId = userId;
                    e.UserName = userName;
                }
            }
            return true;
        }

        /// <summary>The device's view: its status, without using it up.</summary>
        public string Status(int siteId, string code, string secret)
        {
            var e = Find(siteId, code);
            if (e == null || !SecretMatches(e, secret)) return "expired";
            return e.Status;
        }

        /// <summary>An approved request becomes used: the caller signs in as its user. Once only.</summary>
        public Entry Redeem(int siteId, string code, string secret)
        {
            var e = Find(siteId, code);
            if (e == null || !SecretMatches(e, secret)) return null;
            lock (e)
            {
                if (e.Status != "approved") return null;
                e.Status = "used";
            }
            return e;
        }

        public static string Format(string code) => code.Length == 8 ? code[..4] + "-" + code[4..] : code;

        public static string Normalize(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var key = new string(code.ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
            return key.Length == 8 ? key : null;
        }

        /// <summary>"Chrome on a Samsung TV", "Edge on Windows" - what the phone shows.</summary>
        public static string DescribeDevice(string ua)
        {
            ua ??= "";
            string Has(params string[] marks) => marks.FirstOrDefault(m => ua.Contains(m, StringComparison.OrdinalIgnoreCase));

            var platform =
                Has("Tizen") != null ? "a Samsung TV" :
                Has("Web0S", "WebOS", "NetCast") != null ? "an LG TV" :
                Has("AFT") != null && Has("Android") != null ? "a Fire TV" :
                Has("BRAVIA") != null ? "a Sony TV" :
                Has("Android TV", "GoogleTV", "SMART-TV", "SmartTV", "HbbTV") != null ? "a smart TV" :
                Has("Xbox") != null ? "an Xbox" :
                Has("PlayStation") != null ? "a PlayStation" :
                Has("iPad") != null ? "an iPad" :
                Has("iPhone") != null ? "an iPhone" :
                Has("Android") != null ? "Android" :
                Has("Windows") != null ? "Windows" :
                Has("Mac OS X", "Macintosh") != null ? "a Mac" :
                Has("CrOS") != null ? "a Chromebook" :
                Has("Linux") != null ? "Linux" : null;
            var browser =
                Has("SamsungBrowser") != null ? "Samsung Internet" :
                Has("Edg/") != null ? "Edge" :
                Has("OPR/", "Opera") != null ? "Opera" :
                Has("Firefox/") != null ? "Firefox" :
                Has("Chrome/", "CriOS") != null ? "Chrome" :
                Has("Safari/") != null ? "Safari" : null;
            return (browser, platform) switch
            {
                (null, null) => "An unknown device",
                (null, _) => "A browser on " + platform,
                (_, null) => browser,
                _ => browser + " on " + platform,
            };
        }

        private static bool SecretMatches(Entry e, string secret) =>
            !string.IsNullOrEmpty(secret) && CryptographicOperations.FixedTimeEquals(e.SecretHash, Hash(secret));

        private static byte[] Hash(string secret) => SHA256.HashData(Encoding.UTF8.GetBytes(secret));

        private static string NewCode()
        {
            var chars = new char[8];
            for (var i = 0; i < chars.Length; i++) chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
            return new string(chars);
        }

        private static string Base64Url(byte[] bytes) =>
            Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        private void Sweep()
        {
            // a used, denied or old request has nothing left to give: drop it (expired ones are already invisible)
            foreach (var (key, e) in _entries)
            {
                if (Expired(e) || (e.Status is "used" or "denied" && Now - e.CreatedUtc > TimeSpan.FromMinutes(1)))
                {
                    _entries.TryRemove(key, out _);
                }
            }
        }
    }
}
