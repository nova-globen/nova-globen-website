using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Didin.Server.Services
{
    /// <summary>
    /// Cast tickets: a Chromecast or Google TV fetches the film itself, without the user's cookie or token, so it gets a
    /// ticket instead - one file's stream token on one site, sealed by the host's data protection and good for 12 hours.
    /// A ticket opens that file and its subtitles ({site}/api/cast/{ticket}/...) and nothing else; the user's own sign-in
    /// never leaves the page.
    /// </summary>
    public sealed class CastTickets
    {
        public static readonly TimeSpan Lifetime = TimeSpan.FromHours(12);
        private readonly ITimeLimitedDataProtector _protector;

        public CastTickets(IDataProtectionProvider provider) =>
            _protector = provider.CreateProtector("Didin.Cast.v1").ToTimeLimitedDataProtector();

        public string Issue(DidinSite site, string fileToken) => _protector.Protect($"{site.SiteId}:{fileToken}", Lifetime);

        /// <summary>The file token a ticket was issued for on this site; null when it is forged, damaged, expired or another site's.</summary>
        public string Read(DidinSite site, string ticket)
        {
            if (string.IsNullOrEmpty(ticket)) return null;
            string sealedValue;
            try { sealedValue = _protector.Unprotect(ticket); }
            catch (CryptographicException) { return null; }
            var prefix = site.SiteId + ":";
            return sealedValue.StartsWith(prefix, StringComparison.Ordinal) ? sealedValue[prefix.Length..] : null;
        }
    }
}
