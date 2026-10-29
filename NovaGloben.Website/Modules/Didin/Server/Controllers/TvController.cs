using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Services;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Stomme.Enums;
using Stomme.Infrastructure;
using Stomme.Infrastructure.Security;
using Stomme.Managers;
using Stomme.Models;
using Stomme.Shared;

namespace Didin.Server.Controllers
{
    /// <summary>
    /// The TV version: plain server-rendered pages for browsers too old for the Blazor site - a 2019 Samsung TV runs
    /// Chromium 63, where .NET 10's blazor.web.js cannot start. Sign in (form or phone QR), browse, a title, play - with
    /// only ES5 script (no arrow functions, optional chaining or catch without a binding), big focusable tiles for the
    /// remote, and the same catalogue, streams, progress and sign-in as the site. Any browser may open {site}/tv. Its
    /// routes carry the site's folder: the host routes a folder site's api, pages and files for it, and nothing else.
    /// </summary>
    [ApiController]
    [AllowAnonymous]
    public sealed class TvController(CatalogQuery query, DidinStore store, IUserManager users, UserManager<IdentityUser> identityUsers,
        SignInManager<IdentityUser> signIn, ILogManager log, TimeProvider clock) : DidinController
    {
        private const string Prefix = DidinSite.Folder + "/tv";
        private const int PageSize = 48;
        private static readonly HtmlEncoder E = HtmlEncoder.Default;

        private bool SignedIn => User.Identity?.IsAuthenticated == true;
        private int? UserId => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;

        // ------------------------------------------------------------------ browse (or sign in)
        // GET {site}/tv[?q=..][&kind=movie|series][&page=n]
        [HttpGet(Prefix)]
        public async Task<IActionResult> Home([FromQuery] string q, [FromQuery] string kind, [FromQuery] int page = 1, [FromQuery] string error = null,
            [FromQuery] string signedin = null)
        {
            if (!SignedIn)
            {
                if (signedin == "1")
                {
                    log.Log(LogLevel.Warning, this, LogFunction.Security,
                        "TV sign-in did not stay: scheme {Scheme}, cookies sent {Cookies}, browser {Agent}",
                        Request.Scheme, string.Join(", ", Request.Cookies.Keys), Request.Headers.UserAgent.ToString());
                    error = "cookie";
                }
                return Html("Sign in", SignInBody(error), "signin");
            }
            if (signedin == "1") return LocalRedirect(Site.Url("/tv"));   // a clean address to come back to

            var catalog = await query.GetCatalogAsync(Site, HttpContext.RequestAborted);
            var cards = catalog.Cards.AsEnumerable();
            var seen = new HashSet<int>();
            var body = new StringBuilder();
            var state = UserId is int uid ? await LoadStateAsync(uid) : null;
            if (state != null) seen = state.Seen.Select(m => m.TitleId).ToHashSet();

            var searching = !string.IsNullOrWhiteSpace(q);
            if (kind is "movie" or "series") cards = cards.Where(c => c.Kind == kind);
            if (searching)
            {
                var needle = q.Trim();
                cards = cards.Where(c => Contains(c.Title, needle) || Contains(c.OriginalTitle, needle));
            }
            else
            {
                cards = cards.Where(c => !seen.Contains(c.Id));   // seen titles leave the lists; a search finds them
            }

            body.Append("<form class=\"search\" method=\"get\" action=\"/tv\">")
                .Append("<input type=\"search\" name=\"q\" placeholder=\"Search\" value=\"").Append(E.Encode(q ?? "")).Append("\">");
            if (!string.IsNullOrEmpty(kind)) body.Append("<input type=\"hidden\" name=\"kind\" value=\"").Append(E.Encode(kind)).Append("\">");
            body.Append("<button type=\"submit\">Search</button></form>");
            body.Append("<nav class=\"tabs\">")
                .Append(Tab("All", null, kind, q)).Append(Tab("Movies", "movie", kind, q)).Append(Tab("Series", "series", kind, q))
                .Append("</nav>");

            if (!searching && page <= 1 && state != null)
            {
                var going = state.Progress
                    .Where(kv => kv.Value.TitleId != null && kv.Value.Position >= 30 && (kv.Value.Duration <= 0 || kv.Value.Position < kv.Value.Duration * 0.95))
                    .OrderByDescending(kv => kv.Value.UpdatedUtc)
                    .Select(kv => kv.Value.TitleId.Value).Distinct().Where(id => !seen.Contains(id)).Take(12).ToList();
                var byId = catalog.Cards.ToDictionary(c => c.Id);
                if (going.Count > 0)
                {
                    body.Append("<h2>Continue watching</h2><div class=\"grid\">");
                    foreach (var id in going) if (byId.TryGetValue(id, out var c)) body.Append(Tile(c));
                    body.Append("</div>");
                }
            }

            var ordered = (searching ? cards.OrderBy(c => c.Title) : cards.OrderByDescending(c => c.Id)).ToList();
            body.Append("<h2>").Append(searching ? $"Results for “{E.Encode(q.Trim())}”" : "All titles")
                .Append(" <span class=\"count\">").Append(ordered.Count).Append("</span></h2>");
            if (ordered.Count == 0) body.Append("<p class=\"note\">Nothing found.</p>");
            body.Append("<div class=\"grid\">");
            foreach (var c in ordered.Skip((Math.Max(page, 1) - 1) * PageSize).Take(PageSize)) body.Append(Tile(c, seen.Contains(c.Id)));
            body.Append("</div>");
            if (page * PageSize < ordered.Count)
            {
                body.Append("<p><a class=\"button\" href=\"").Append(E.Encode(Link(q, kind, page + 1))).Append("\">More titles</a></p>");
            }
            return Html("Didin", body.ToString(), "home");
        }

        // ------------------------------------------------------------------ a title
        // GET {site}/tv/title/{id}
        [HttpGet(Prefix + "/title/{id:int}")]
        public async Task<IActionResult> Title(int id)
        {
            if (!SignedIn) return LocalRedirect(Site.Url("/tv"));
            var t = await query.GetDetailsAsync(Site, id, HttpContext.RequestAborted);
            if (t == null) return NotFound();
            var b = new StringBuilder();
            b.Append("<p><a class=\"button\" href=\"/tv\">&larr; Back</a></p><div class=\"title\">");
            if (!string.IsNullOrEmpty(t.PosterUrl)) b.Append("<img class=\"poster\" src=\"").Append(E.Encode(t.PosterUrl)).Append("\" alt=\"\">");
            b.Append("<div class=\"facts\"><h1 dir=\"auto\">").Append(E.Encode(t.Title));
            if (t.Year != null) b.Append(" <span class=\"dim\">(").Append(t.Year).Append(")</span>");
            b.Append("</h1><p class=\"dim\">")
             .Append(E.Encode(string.Join(" · ", new[] { string.Join(", ", t.Genres.Take(3)), t.RuntimeMinutes is int m ? $"{m} min" : null,
                 t.ImdbRating is double r ? $"IMDb {r.ToString("0.0", CultureInfo.InvariantCulture)}" : null }.Where(x => !string.IsNullOrEmpty(x)))))
             .Append("</p>");
            if (!string.IsNullOrEmpty(t.Plot)) b.Append("<p class=\"plot\" dir=\"auto\">").Append(E.Encode(t.Plot)).Append("</p>");

            if (t.Kind == "series")
            {
                foreach (var season in t.Seasons)
                {
                    b.Append("<h2>Season ").Append(season.Season).Append("</h2><div class=\"episodes\">");
                    foreach (var ep in season.Episodes)
                    {
                        var f = ep.Files.FirstOrDefault(x => x.Playback != "damaged");
                        if (f == null) continue;
                        b.Append("<a class=\"episode\" href=\"/tv/play/").Append(f.Token).Append("\">")
                         .Append(string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00}", ep.Season, ep.Number))
                         .Append(" <span dir=\"auto\">").Append(E.Encode(ep.Name ?? "")).Append("</span></a>");
                    }
                    b.Append("</div>");
                }
            }
            else
            {
                b.Append("<div class=\"actions\">");
                foreach (var f in t.Files.Where(x => x.Role == null && x.Playback != "damaged").OrderBy(x => x.Part ?? 0))
                {
                    b.Append("<a class=\"button play\" href=\"/tv/play/").Append(f.Token).Append("\">&#9654; ")
                     .Append(f.Part != null ? $"Part {f.Part}" : "Play").Append("</a>");
                }
                b.Append("</div>");
            }
            b.Append("</div></div>");
            return Html(t.Title, b.ToString(), "title");
        }

        // ------------------------------------------------------------------ play
        // GET {site}/tv/play/{token}  - full screen, remembers where it stopped, then plays on to the next part or episode
        [HttpGet(Prefix + "/play/{token}")]
        public async Task<IActionResult> Play(string token)
        {
            if (!SignedIn) return LocalRedirect(Site.Url("/tv"));
            var fileId = FileToken.Decode(token);
            if (fileId == null) return BadRequest();
            var file = await query.GetFileAsync(Site, fileId, HttpContext.RequestAborted);
            if (file?.TitleId is not int titleId) return NotFound();
            var t = await query.GetDetailsAsync(Site, titleId, HttpContext.RequestAborted);
            if (t == null) return NotFound();

            // the order this file belongs to: a film's parts, or every episode in order
            var sequence = t.Kind == "series"
                ? t.Seasons.SelectMany(s => s.Episodes).Select(ep => (Info: ep.Files.FirstOrDefault(x => x.Playback != "damaged"),
                    Label: string.Format(CultureInfo.InvariantCulture, "S{0:00}E{1:00} {2}", ep.Season, ep.Number, ep.Name))).Where(x => x.Info != null).ToList()
                : t.Files.Where(x => x.Role == null && x.Playback != "damaged").OrderBy(x => x.Part ?? 0)
                    .Select(x => (Info: x, Label: x.Part != null ? $"Part {x.Part}" : "")).ToList();
            var index = sequence.FindIndex(x => x.Info.Token == token);
            var current = index >= 0 ? sequence[index] : (Info: t.Files.Concat(t.Seasons.SelectMany(s => s.Episodes).SelectMany(e => e.Files)).FirstOrDefault(x => x.Token == token), Label: "");
            var next = index >= 0 && index + 1 < sequence.Count ? sequence[index + 1].Info.Token : null;

            double resume = 0;
            if (UserId is int uid)
            {
                var state = await LoadStateAsync(uid);
                if (state != null && state.Progress.TryGetValue(fileId, out var p) && p.Position >= 30 && (p.Duration <= 0 || p.Position < p.Duration * 0.95))
                {
                    resume = p.Position;
                }
            }

            var b = new StringBuilder();
            b.Append("<div class=\"player\"><video id=\"v\" src=\"/api/stream/").Append(token).Append("\" controls autoplay preload=\"auto\">");
            foreach (var sub in current.Info?.Subtitles ?? new List<SubtitleTrack>())
            {
                b.Append("<track kind=\"subtitles\" src=\"").Append(E.Encode(sub.Url)).Append("\" srclang=\"").Append(E.Encode(sub.Lang ?? "und"))
                 .Append("\" label=\"").Append(E.Encode(sub.Label ?? sub.Lang ?? "")).Append("\">");
            }
            b.Append("</video><div class=\"bar\"><a class=\"button\" href=\"/tv/title/").Append(titleId).Append("\">&larr; Back</a> <span dir=\"auto\">")
             .Append(E.Encode(t.Title)).Append(' ').Append(E.Encode(current.Label ?? "")).Append("</span>");
            if (next != null) b.Append(" <a class=\"button\" href=\"/tv/play/").Append(next).Append("\">Next &rarr;</a>");
            b.Append("</div>");
            if (current.Info?.Playback == "transcode")
            {
                b.Append("<p class=\"note\">This file's format may not play in this browser.</p>");
            }
            b.Append("</div>");
            // ES5 only (Chromium 63): resume, save progress every 15 s and on leaving, then the next part or episode
            b.Append("<script>(function(){var v=document.getElementById('v');var token='").Append(token).Append("';var resume=")
             .Append(resume.ToString("0", CultureInfo.InvariantCulture)).Append(";var next=").Append(next == null ? "null" : "'" + next + "'").Append(";")
             .Append("function save(){if(!v.duration||isNaN(v.duration))return;var x=new XMLHttpRequest();x.open('POST','/tv/progress/'+token,true);")
             .Append("x.setRequestHeader('Content-Type','application/x-www-form-urlencoded');x.send('position='+Math.floor(v.currentTime)+'&duration='+Math.floor(v.duration));}")
             .Append("v.addEventListener('loadedmetadata',function(){if(resume>0&&resume<v.duration-10){v.currentTime=resume;}});")
             .Append("setInterval(function(){if(!v.paused)save();},15000);v.addEventListener('pause',save);window.addEventListener('pagehide',save);")
             .Append("v.addEventListener('ended',function(){save();if(next){location.href='/tv/play/'+next;}});")
             .Append("try{v.focus();}catch(e){}})();</script>");
            return Html(t.Title, b.ToString(), "play");
        }

        // POST {site}/tv/progress/{token}  (form: position, duration) - the TV player's progress; same record as api/me/progress
        [HttpPost(Prefix + "/progress/{token}")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> Progress(string token, [FromForm] double position, [FromForm] double duration)
        {
            if (UserId is not int userId) return Unauthorized();
            if (position < 0 || double.IsNaN(position) || double.IsNaN(duration)) return BadRequest();
            var file = await query.GetFileAsync(Site, FileToken.Decode(token), HttpContext.RequestAborted);
            if (file == null) return NotFound();
            var state = await LoadStateAsync(userId) ?? new UserStateDoc { Id = $"userstate/{userId}", UserId = userId };
            state.Progress[file.Id] = new ProgressEntry { TitleId = file.TitleId, EpisodeId = file.EpisodeId, Position = position, Duration = duration, UpdatedUtc = clock.GetUtcNow().UtcDateTime };
            if (file.TitleId is int titleId && duration > 0 && position < duration * 0.5) state.FinishedSkipped.Remove(titleId);
            await store.StoreAsync(Site.SiteId, DidinKind.UserState, userId, 0, state, HttpContext.RequestAborted);
            return NoContent();
        }

        // ------------------------------------------------------------------ sign in / out
        // POST {site}/tv/signin (form: username, password) - checked by Stomme (lockout, failed-attempt counting), then the
        // browser's sign-in cookie, kept: a TV stays signed in
        [HttpPost(Prefix + "/signin")]
        [IgnoreAntiforgeryToken]
        [EnableRateLimiting(AccountRateLimiting.SignInPolicy)]
        public async Task<IActionResult> SignIn([FromForm] string username, [FromForm] string password)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password)) return LocalRedirect(Site.Url("/tv?error=empty"));
            var user = await users.LoginUser(new User
            {
                SiteId = Site.SiteId,
                Username = username.Trim(),
                Password = password,
                LastIPAddress = HttpContext.Connection.RemoteIpAddress?.ToString()
            }, false, false);
            if (user == null || !user.IsAuthenticated) return LocalRedirect(Site.Url("/tv?error=failed"));
            var identityUser = await identityUsers.FindByNameAsync(user.Username);
            if (identityUser == null) return LocalRedirect(Site.Url("/tv?error=failed"));
            await signIn.SignInAsync(identityUser, isPersistent: true);
            log.Log(LogLevel.Information, this, LogFunction.Security, "TV sign-in for {Username}", user.Username);
            return LocalRedirect(Site.Url("/tv?signedin=1"));
        }

        [HttpPost(Prefix + "/signout")]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> SignOutHere()
        {
            await signIn.SignOutAsync();
            return LocalRedirect(Site.Url("/tv"));
        }

        // ------------------------------------------------------------------ helpers
        private Task<UserStateDoc> LoadStateAsync(int userId) =>
            store.LoadAsync<UserStateDoc>(Site.SiteId, DidinKind.UserState, userId, ct: HttpContext.RequestAborted);

        private static bool Contains(string text, string needle) =>
            !string.IsNullOrEmpty(text) && CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, needle, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;

        private string Link(string q, string kind, int page)
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(q)) parts.Add("q=" + Uri.EscapeDataString(q.Trim()));
            if (!string.IsNullOrEmpty(kind)) parts.Add("kind=" + Uri.EscapeDataString(kind));
            if (page > 1) parts.Add("page=" + page);
            return Site.Url("/tv") + (parts.Count > 0 ? "?" + string.Join("&", parts) : "");
        }

        private string Tab(string label, string value, string current, string q) =>
            $"<a class=\"tab{((current ?? "") == (value ?? "") ? " on" : "")}\" href=\"{E.Encode(Link(q, value, 1))}\">{label}</a>";

        private string Tile(CatalogCard c, bool seen = false)
        {
            var b = new StringBuilder("<a class=\"tile\" href=\"/tv/title/").Append(c.Id).Append("\">");
            if (!string.IsNullOrEmpty(c.PosterUrl)) b.Append("<img src=\"").Append(E.Encode(c.PosterUrl)).Append("\" alt=\"\" loading=\"lazy\">");
            else b.Append("<span class=\"noposter\">").Append(E.Encode(c.Title.Length > 0 ? c.Title[..1] : "?")).Append("</span>");
            b.Append("<span class=\"name\" dir=\"auto\">").Append(E.Encode(c.Title)).Append("</span><span class=\"dim\">")
             .Append(c.Year?.ToString() ?? "").Append(c.Kind == "series" ? " · Series" : "").Append(seen ? " · Seen" : "").Append("</span></a>");
            return b.ToString();
        }

        private string SignInBody(string error)
        {
            var b = new StringBuilder("<div class=\"signin\"><div class=\"card\"><h1>Didin</h1>");
            if (error == "failed") b.Append("<p class=\"error\">Sign-in failed. Check your username and password.</p>");
            if (error == "cookie") b.Append("<p class=\"error\">You were signed in, but this TV's browser did not keep it. In the browser's settings, allow cookies (or turn off private browsing), then sign in again.</p>");
            else if (error == "empty") b.Append("<p class=\"error\">Enter your username and password.</p>");
            b.Append("<form method=\"post\" action=\"/tv/signin\"><label>Username<input name=\"username\" autocomplete=\"username\"></label>")
             .Append("<label>Password<input name=\"password\" type=\"password\" autocomplete=\"current-password\"></label>")
             .Append("<button type=\"submit\">Sign in</button></form></div>");
            // sign in with your phone, in ES5: the same api/link endpoints as the site's QR panel
            b.Append("<div class=\"card qr\"><h2>Sign in with your phone</h2><img id=\"qr\" alt=\"QR code to sign in with your phone\">")
             .Append("<p class=\"dim\">Scan it with your phone's camera and approve this TV. Or go to <b id=\"qrurl\"></b> and enter <b id=\"qrcode\"></b></p>")
             .Append("<p id=\"qrstatus\" class=\"dim\"></p></div></div>")
             .Append("<script>(function(){var st=document.getElementById('qrstatus');function req(m,u,body,cb){var x=new XMLHttpRequest();x.open(m,u,true);")
             .Append("x.setRequestHeader('Content-Type','application/json');x.onreadystatechange=function(){if(x.readyState===4){cb(x.status,x.responseText);}};x.send(body);}")
             .Append("function start(){req('POST','/api/link/start','{}',function(s,t){if(s!==200){st.innerHTML='Phone sign-in is not available right now.';return;}")
             .Append("var d=JSON.parse(t);document.getElementById('qr').src=d.qr;document.getElementById('qrcode').innerHTML=d.code;")
             .Append("document.getElementById('qrurl').innerHTML=d.url.replace(/^https?:\\/\\//,'');st.innerHTML='Waiting for your phone...';var until=Date.now()+d.expiresIn*1000;")
             .Append("function poll(){if(Date.now()>until){st.innerHTML='This code has expired. <a href=\"/tv\">Show a new one</a>';return;}")
             .Append("req('POST','/api/link/poll',JSON.stringify({code:d.code,secret:d.secret}),function(s2,t2){var r=s2===200?JSON.parse(t2).status:'pending';")
             .Append("if(r==='approved'){st.innerHTML='Approved. Signing in...';var f=document.createElement('form');f.method='post';f.action='/api/link/signin';")
             .Append("var a=document.createElement('input');a.type='hidden';a.name='code';a.value=d.code;f.appendChild(a);")
             .Append("var c=document.createElement('input');c.type='hidden';c.name='secret';c.value=d.secret;f.appendChild(c);")
             .Append("var n=document.createElement('input');n.type='hidden';n.name='next';n.value='/tv?signedin=1';f.appendChild(n);document.body.appendChild(f);f.submit();return;}")
             .Append("if(r==='denied'){st.innerHTML='Denied on the phone. <a href=\"/tv\">Show a new code</a>';return;}")
             .Append("if(r==='expired'||r==='used'){st.innerHTML='This code has expired. <a href=\"/tv\">Show a new one</a>';return;}")
             .Append("setTimeout(poll,(d.interval||3)*1000);});}setTimeout(poll,(d.interval||3)*1000);});}start();})();</script>");
            return b.ToString();
        }

        private IActionResult Html(string title, string body, string page)
        {
            var who = SignedIn ? User.FindFirst(ClaimTypes.Name)?.Value : null;
            var sb = new StringBuilder();
            sb.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
              .Append("<title>").Append(E.Encode(title)).Append(" - Didin</title><style>").Append(Css).Append("</style></head><body class=\"").Append(page).Append("\">")
              .Append("<header><a class=\"brand\" href=\"/tv\">Didin</a>");
            if (who != null)
            {
                sb.Append("<span class=\"who\">").Append(E.Encode(who)).Append("</span><form method=\"post\" action=\"/tv/signout\"><button type=\"submit\">Sign out</button></form>");
            }
            sb.Append("</header><main>").Append(body).Append("</main></body></html>");
            Response.Headers.CacheControl = "no-store";
            return Content(OnSite(sb.ToString()), "text/html; charset=utf-8");
        }

        /// <summary>
        /// The pages are written with root-absolute addresses ("/tv/...", "/api/..."), quoted in the markup and the script;
        /// they move under the site's folder here. Text from the catalogue is HTML-encoded, so it has no raw quote to match.
        /// </summary>
        private string OnSite(string html)
        {
            foreach (var root in new[] { "/tv", "/api/" })
            {
                html = html.Replace("\"" + root, "\"" + Site.Url(root)).Replace("'" + root, "'" + Site.Url(root));
            }
            return html;
        }

        // no custom properties, flexbox and grid only - Chromium 63 has them; sizes for a TV across the room
        private const string Css = @"
*{box-sizing:border-box}html{background:#1e1c1c}body{margin:0;background:#1e1c1c;color:#f4eef4;font:22px/1.45 'Segoe UI',Roboto,Arial,sans-serif}
a{color:#e877ea}header{display:flex;align-items:center;padding:18px 40px;border-bottom:1px solid #3a3434}header>*{margin-left:20px}header>.brand{margin-left:0}
.brand{font-weight:700;font-size:30px;color:#fff;text-decoration:none;flex:1}.who{color:#b5a9b5}
main{padding:24px 40px 60px}h1{margin:0 0 8px;font-size:40px}h2{margin:28px 0 14px;font-size:28px}.dim{color:#b5a9b5}.count{color:#b5a9b5;font-size:22px;font-weight:400}
button,.button,input[type=submit]{display:inline-block;padding:12px 24px;border-radius:12px;border:2px solid #4a4040;background:#2e2929;color:#fff;font:inherit;text-decoration:none;cursor:pointer}
button[type=submit],.play{background:#c900cc;border-color:#c900cc}
a:focus,button:focus,input:focus,.tile:focus{outline:4px solid #f29bf3;outline-offset:3px}
input{font:inherit;padding:12px 16px;border-radius:12px;border:2px solid #4a4040;background:#2a2626;color:#fff}
.search{display:flex;margin-bottom:16px}.search button{margin-left:12px}.search input{flex:1;max-width:700px}
.tabs{display:flex}.tabs .tab{margin-right:12px}.tab{padding:8px 20px;border-radius:999px;color:#fff;text-decoration:none;border:2px solid #4a4040}.tab.on{background:#c900cc;border-color:#c900cc}
.grid{display:flex;flex-wrap:wrap}.grid .tile{margin:0 22px 22px 0}
.tile{display:flex;flex-direction:column;width:200px;text-decoration:none;color:#fff;border-radius:14px;padding:6px}
.tile img,.noposter{width:188px;height:282px;object-fit:cover;border-radius:12px;background:#2a2626}
.noposter{display:flex;align-items:center;justify-content:center;font-size:60px;color:#b5a9b5}
.tile .name{margin-top:8px;font-weight:600;overflow:hidden;text-overflow:ellipsis;white-space:nowrap}.tile .dim{font-size:18px}
.title{display:flex;align-items:flex-start}.title .poster{margin-right:40px}.poster{width:340px;border-radius:18px}.facts{flex:1;min-width:0}.plot{max-width:1100px}
.actions,.episodes{display:flex;flex-wrap:wrap}.actions>*,.episodes>*{margin:0 14px 14px 0}.episode{display:block;padding:12px 18px;border-radius:12px;background:#2e2929;color:#fff;text-decoration:none;min-width:420px}
.player video{position:fixed;top:0;left:0;width:100%;height:100%;background:#000}
.player .bar>*{margin-right:18px}.player .bar{position:fixed;top:0;left:0;right:0;padding:20px 30px;display:flex;align-items:center;background:linear-gradient(rgba(0,0,0,.75),rgba(0,0,0,0))}
body.play header{display:none}body.play main{padding:0}.note{color:#ffb4a6}
.signin{display:flex;justify-content:center;align-items:flex-start;padding-top:40px}.signin .card{margin:0 20px}
.card{background:#262222;border:1px solid #3a3434;border-radius:20px;padding:36px;width:520px}
.card label{display:block;margin-bottom:18px}.card label input{display:block;width:100%;margin-top:6px}.card button{width:100%}
.qr img{display:block;width:300px;height:300px;margin:12px auto;background:#fff;border-radius:16px;padding:12px}.error{color:#ffb4a6}
";
    }
}
