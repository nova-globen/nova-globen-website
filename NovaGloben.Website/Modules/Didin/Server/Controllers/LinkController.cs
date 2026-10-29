using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Didin.Server.Services;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using QRCoder;
using Stomme.Enums;
using Stomme.Infrastructure;
using Stomme.Infrastructure.Security;
using Stomme.Managers;
using Stomme.Shared;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// "Sign in with your phone". A browser (a TV's, most often) starts a request and shows its QR code; a phone that is
    /// signed in to the same site opens it, sees which device asks, and approves; the browser then signs in as that user
    /// through a form post that sets the cookie. The short code alone cannot sign anything in: that takes the secret the
    /// browser keeps. Antiforgery does not apply to the anonymous calls: they read no cookie, and the sign-in post carries
    /// the browser's own secret. Every address is under the site's api/, which the host routes for a folder site.
    /// </summary>
    [ApiController]
    [Route("api/link")]
    public sealed class LinkController(DeviceLinks links, IUserManager users, UserManager<IdentityUser> identityUsers,
        SignInManager<IdentityUser> signIn, ILogManager log, TimeProvider clock) : DidinController
    {
        public sealed class DeviceRequest { public string Code { get; set; } public string Secret { get; set; } }

        // POST {site}/api/link/start  ->  {code, secret, url, qr, expiresIn, interval}
        [HttpPost("start")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting(AccountRateLimiting.SignInPolicy)]
        public IActionResult Start()
        {
            var started = links.Start(Site.SiteId, Request.Headers.UserAgent.ToString(), RemoteIp());
            if (started == null) return StatusCode(503, "Too many sign-in requests right now. Try again in a few minutes.");
            var (entry, secret) = started.Value;
            return Ok(new
            {
                code = DeviceLinks.Format(entry.Code),
                secret,
                url = OpenUrl(),
                qr = Site.Url($"/api/link/qr/{entry.Code}"),
                expiresIn = (int)DeviceLinks.Lifetime.TotalSeconds,
                interval = 3,
            });
        }

        // GET {site}/api/link/qr/{code}  ->  SVG of the open address, for a pending request only (not a QR service)
        [HttpGet("qr/{code}")]
        [AllowAnonymous]
        public IActionResult Qr(string code)
        {
            var entry = links.Find(Site.SiteId, code);
            if (entry == null || entry.Status != "pending") return NotFound();
            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode($"{OpenUrl()}?code={entry.Code}", QRCodeGenerator.ECCLevel.M);
            var svg = new SvgQRCode(data).GetGraphic(8, "#000000", "#ffffff", true);
            Response.Headers.CacheControl = "no-store";
            return Content(svg, "image/svg+xml");
        }

        // POST {site}/api/link/poll {code, secret}  ->  {status}; an approved browser then signs in with a form post
        [HttpPost("poll")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        public IActionResult Poll([FromBody] DeviceRequest request) =>
            Ok(new { status = links.Status(Site.SiteId, request?.Code, request?.Secret) });

        // POST {site}/api/link/signin (form: code, secret, next?)  ->  the browser's auth cookie, then the site's home or
        // its TV pages. The browser stays signed in.
        [HttpPost("signin")]
        [AllowAnonymous]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting(AccountRateLimiting.SignInPolicy)]
        public async Task<IActionResult> SignIn([FromForm] string code, [FromForm] string secret, [FromForm] string next = null)
        {
            var home = Site.Url("/");
            // the TV pages ask to come back to themselves; nothing else may name a page here
            if (next != null && next.StartsWith(Site.Url("/tv"), StringComparison.Ordinal) && Url.IsLocalUrl(next)) home = next;
            var redeemed = links.Redeem(Site.SiteId, code, secret);
            if (redeemed == null) return LocalRedirect(Site.Url("/login"));
            var user = await users.GetUserAsync(redeemed.UserId, Site.SiteId);
            var identityUser = user == null || user.IsDeleted ? null : await identityUsers.FindByNameAsync(user.Username);
            if (identityUser == null) return LocalRedirect(Site.Url("/login"));
            await signIn.SignInAsync(identityUser, isPersistent: true);
            log.Log(LogLevel.Information, this, LogFunction.Security, "Phone sign-in: {Device} signed in as {Username}", redeemed.Device, user.Username);
            return LocalRedirect(home);
        }

        // GET {site}/api/link/open?code=...  - where the QR code points. Signed in: the approval on the site's home page;
        // otherwise sign in first.
        [HttpGet("open")]
        [AllowAnonymous]
        public IActionResult Open(string code)
        {
            var key = DeviceLinks.Normalize(code) ?? "";
            var target = Site.Url("/?link=" + Uri.EscapeDataString(key == "" ? "" : DeviceLinks.Format(key)));
            if (User.Identity?.IsAuthenticated == true) return LocalRedirect(target);
            return LocalRedirect(Site.Url("/login?returnurl=" + Uri.EscapeDataString(target)));
        }

        // GET {site}/api/link/{code}  ->  LinkInfo, for the phone that is about to approve
        [HttpGet("{code}")]
        [Authorize]
        public ActionResult<LinkInfo> Info(string code)
        {
            var entry = links.Find(Site.SiteId, code);
            if (entry == null) return NotFound();
            return Describe(entry);
        }

        // POST {site}/api/link/{code}/decision {"approve": true|false}  ->  LinkInfo after the decision; the signed-in
        // user signs the device in as themselves
        [HttpPost("{code}/decision")]
        [Authorize]
        public ActionResult<LinkInfo> Decide(string code, [FromBody] LinkDecision decision)
        {
            if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId)) return Unauthorized();
            var name = User.FindFirst(ClaimTypes.Name)?.Value;
            if (!links.Decide(Site.SiteId, code, decision?.Approve == true, userId, name)) return NotFound();
            return Describe(links.Find(Site.SiteId, code));
        }

        private LinkInfo Describe(DeviceLinks.Entry entry) => new()
        {
            Code = DeviceLinks.Format(entry.Code),
            Status = entry.Status,
            Device = entry.Device,
            SameNetwork = entry.Ip != null && entry.Ip == RemoteIp(),
            AgeSeconds = (int)(clock.GetUtcNow().UtcDateTime - entry.CreatedUtc).TotalSeconds,
        };

        /// <summary>The site's own open address: https outside the developer's machine (TLS ends at the proxy).</summary>
        private string OpenUrl()
        {
            var host = Request.Host.Host;
            var local = host is "localhost" or "127.0.0.1" or "[::1]";
            return $"{(local ? Request.Scheme : "https")}://{Request.Host}{Site.Url("/api/link/open")}";
        }

        private string RemoteIp()
        {
            // behind Cloudflare the visitor's address is in CF-Connecting-IP; otherwise the connection's
            var cf = Request.Headers["CF-Connecting-IP"].ToString();
            return !string.IsNullOrEmpty(cf) ? cf : HttpContext.Connection.RemoteIpAddress?.ToString();
        }
    }
}
