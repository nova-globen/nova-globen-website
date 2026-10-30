using System;
using System.Threading.Tasks;
using Microsoft.JSInterop;

namespace Didin.Module.Media
{
    /// <summary>
    /// localStorage catalog cache + fullscreen helpers, implemented as small
    /// eval snippets: the host strips script tags from HeadContent and does not
    /// auto-include internal module JS, so no external script can be relied on.
    /// Every call is best-effort - caching must never break the app.
    /// </summary>
    public class Interop
    {
        private readonly IJSRuntime _js;
        public Interop(IJSRuntime js) => _js = js;

        public async Task<string> LoadCatalogAsync(string version)
        {
            const string code = @"
                (() => { try {
                    var stored = window.localStorage.getItem('didin.catalog');
                    if (!stored) return null;
                    var doc = JSON.parse(stored);
                    return doc && doc.version === arguments[0] ? stored : null;
                } catch (e) { return null; } })()";
            try { return await _js.InvokeAsync<string>("eval", $"(version => {code})('{version}')"); }
            catch { return null; }
        }

        public async Task SaveCatalogAsync(string version, string json)
        {
            try
            {
                var payload = System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(json);
                await _js.InvokeVoidAsync("eval",
                    $"try {{ var d = JSON.parse(\"{payload}\"); d.version = '{version}'; " +
                    "window.localStorage.setItem('didin.catalog', JSON.stringify(d)); } catch (e) { }");
            }
            catch { /* cache is an optimization only */ }
        }

        /// <summary>
        /// Keyboard and TV-remote navigation. A TV browser turns the D-pad into arrow keys, OK into
        /// Enter and Back into Escape (or GoBack / XF86Back / keyCode 461 / 10009, which count as Back too).
        /// - Browsing: an arrow moves focus to the nearest card or control (button, link) in that direction, rows, the
        ///   grid and a title's buttons alike; with nothing focused, an arrow focuses a title's Play, else the first card.
        ///   Back leaves the details page.
        /// - The player: an arrow with nothing focused goes to the video; on the video Left/Right seek 10 s, Up goes to
        ///   the bar, Enter plays/pauses; in the bar Left/Right move between its buttons and Down returns to the video.
        ///   C opens the subtitle menu. Back closes the subtitle menu if it is open, else the player.
        /// - The subtitle menu: Up/Down/Home/End move between its options; Enter/Space/click choose (they are buttons).
        /// Installed once per page; Enter/Space on a card is the card's own handler.
        /// </summary>
        public async Task EnableKeyboardNavigationAsync()
        {
            const string code = @"(() => {
                if (window.__didinKeys) return; window.__didinKeys = true;
                const isBack = e => e.key === 'Escape' || e.key === 'GoBack' || e.key === 'BrowserBack' || e.key === 'XF86Back' || e.keyCode === 461 || e.keyCode === 10009;
                const dirs = { ArrowLeft: [-1, 0], ArrowRight: [1, 0], ArrowUp: [0, -1], ArrowDown: [0, 1] };
                const visible = el => !!el && el.getClientRects().length > 0;
                // What an arrow can land on: the cards and every control of the page. FluentUI's buttons keep their own
                // <button> in a shadow root, which a TV WebView's own D-pad navigation does not reach, so they are listed here.
                const targetSelector = '.bin-card[tabindex], fluent-button:not([disabled]), button:not([disabled]), a[href]';
                const targets = () => [...document.querySelectorAll('.didin .bin-card[tabindex], .didin fluent-button:not([disabled]), .didin button:not([disabled]), .didin a[href]')]
                    .filter(c => c.offsetParent !== null && c.getAttribute('tabindex') !== '-1' && !c.closest('[hidden]'));
                const centre = r => ({ x: r.left + r.width / 2, y: r.top + r.height / 2 });
                const typing = t => t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || (t.closest && t.closest('fluent-text-input, fluent-slider, fluent-dropdown')));
                const inPlayer = (player, e) => {
                    const t = e.target;
                    const menu = document.getElementById('bin-sub-menu');
                    const toggle = document.getElementById('bin-sub-toggle');
                    if (menu) {
                        const opts = [...menu.querySelectorAll('.bin-sub-option')];
                        if (isBack(e)) { if (toggle) { toggle.click(); } e.preventDefault(); return; }
                        const i = opts.indexOf(document.activeElement);
                        let n = -1;
                        if (e.key === 'ArrowDown') n = i < 0 ? 0 : Math.min(opts.length - 1, i + 1);
                        else if (e.key === 'ArrowUp') n = i < 0 ? 0 : Math.max(0, i - 1);
                        else if (e.key === 'Home') n = 0;
                        else if (e.key === 'End') n = opts.length - 1;
                        else if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') { e.preventDefault(); return; }
                        if (n >= 0 && opts[n]) { opts[n].focus(); e.preventDefault(); }
                        return;
                    }
                    if (isBack(e)) { const close = document.getElementById('bin-close'); if (close) { close.click(); e.preventDefault(); } return; }
                    if (typing(t) || e.ctrlKey || e.metaKey || e.altKey) return;
                    if ((e.key === 'c' || e.key === 'C') && toggle) { toggle.click(); e.preventDefault(); return; }
                    const video = document.getElementById('bin-video');
                    if (t && t.tagName === 'VIDEO') {
                        if (e.key === 'Enter') { if (t.paused) { t.play().catch(() => {}); } else { t.pause(); } e.preventDefault(); return; }
                        if (e.key === 'ArrowLeft' || e.key === 'ArrowRight') {
                            if (isFinite(t.duration)) { t.currentTime = Math.max(0, Math.min(t.duration - 1, t.currentTime + (e.key === 'ArrowLeft' ? -10 : 10))); }
                            e.preventDefault(); return;
                        }
                        if (e.key === 'ArrowUp') {
                            const first = visible(toggle) ? toggle : document.getElementById('bin-close');
                            if (first) { first.focus(); e.preventDefault(); }
                        }
                        return;
                    }
                    if (!dirs[e.key]) return;
                    if (!t || !player.contains(t) || t === player) { if (visible(video)) { video.focus(); e.preventDefault(); } return; }
                    if (e.key === 'ArrowDown' || e.key === 'ArrowUp') { if (e.key === 'ArrowDown' && visible(video)) { video.focus(); } e.preventDefault(); return; }
                    const controls = [...player.querySelectorAll('.bin-player-bar button, .bin-player-bar fluent-button')].filter(visible);
                    const j = controls.indexOf(t.closest('button, fluent-button'));
                    const k = e.key === 'ArrowLeft' ? j - 1 : j + 1;
                    if (j >= 0 && controls[k]) { controls[k].focus(); }
                    e.preventDefault();
                };
                document.addEventListener('keydown', e => {
                    const player = document.getElementById('bin-player');
                    if (player) { inPlayer(player, e); return; }
                    if (isBack(e)) {
                        const back = document.getElementById('bin-back');
                        if (back) { back.click(); e.preventDefault(); }
                        return;
                    }
                    const dir = dirs[e.key];
                    if (!dir) return;
                    const t = e.target;
                    if (typing(t) || (t && t.tagName === 'VIDEO')) return;
                    const all = targets();
                    if (!all.length) return;
                    const current = t && t.closest ? t.closest(targetSelector) : null;
                    let next = null;
                    if (!current || !all.includes(current)) {
                        // nothing focused yet: a title's Play, else the first card, else the first control
                        next = document.querySelector('.didin .bin-details fluent-button[appearance=""primary""]')
                            || all.find(c => c.classList.contains('bin-card')) || all[0];
                    }
                    else {
                        const a = centre(current.getBoundingClientRect());
                        let best = Infinity;
                        for (const c of all) {
                            if (c === current) continue;
                            const b = centre(c.getBoundingClientRect());
                            const dx = b.x - a.x, dy = b.y - a.y;
                            const along = dir[0] ? dx * dir[0] : dy * dir[1];
                            const across = dir[0] ? Math.abs(dy) : Math.abs(dx);
                            if (along <= 1) continue;
                            const score = along + across * 3;
                            if (score < best) { best = score; next = c; }
                        }
                    }
                    if (next) { next.focus(); next.scrollIntoView({ block: 'nearest', inline: 'nearest' }); e.preventDefault(); }
                });
            })()";
            try { await _js.InvokeVoidAsync("eval", code); }
            catch { /* navigation by mouse/touch still works */ }
        }

        public async Task FullscreenAsync(string elementId)
        {
            try
            {
                await _js.InvokeVoidAsync("eval",
                    "(() => { var el = document.getElementById('" + elementId + "'); if (!el || document.fullscreenElement === el) return; " +
                    "var rfs = el.requestFullscreen || el.webkitRequestFullscreen || el.msRequestFullscreen; " +
                    // refused without a user gesture (a part that played on), or the element gone by the time it settles
                    "if (rfs) { var p = rfs.call(el); if (p && p.catch) p.catch(function () {}); } })()");
            }
            catch { /* player still works without fullscreen */ }
        }

        public async Task ExitFullscreenAsync()
        {
            try
            {
                await _js.InvokeVoidAsync("eval",
                    "(() => { var efs = document.exitFullscreen || document.webkitExitFullscreen; " +
                    "if (efs && document.fullscreenElement) efs.call(document); })()");
            }
            catch { }
        }

        /// <summary>
        /// Adds a subtitle track from WebVTT text. The text becomes a blob URL, which is same-origin to the
        /// page, so this works where a cross-origin track URL is refused (the MAUI shell). The URL is released once the
        /// track has loaded. <paramref name="trackId"/> is the id the subtitle menu selects it by.
        /// </summary>
        public async Task AddSubtitleTrackAsync(string videoId, string trackId, string vtt, string lang, string label)
        {
            try
            {
                await _js.InvokeVoidAsync("eval", @"window.__didinAddTrack = (videoId, trackId, vtt, lang, label) => {
                    var v = document.getElementById(videoId); if (!v) return;
                    var url = URL.createObjectURL(new Blob([vtt], { type: 'text/vtt' }));
                    var t = document.createElement('track');
                    t.id = trackId; t.kind = 'subtitles'; t.src = url; t.srclang = lang || ''; t.label = label || lang || 'Subtitles';
                    t.addEventListener('load', () => URL.revokeObjectURL(url), { once: true });
                    v.appendChild(t);
                }");
                await _js.InvokeVoidAsync("__didinAddTrack", videoId, trackId, vtt, lang, label);
            }
            catch { /* the video still plays without subtitles */ }
        }

        /// <summary>
        /// The subtitle menu's half in the page. Tracks are chosen by id (the track element's id, which its
        /// TextTrack carries): <c>set</c> shows the wanted one and disables the rest, now and for any track added later
        /// (the MAUI shell adds its tracks as they arrive). A choice made in the browser's own captions menu is reported
        /// back through <c>OnSubtitleChanged</c>, so both menus always agree; our own changes are not echoed, and a track
        /// the browser switches on by itself next to the chosen one is switched off again.
        /// </summary>
        private const string SubtitleBridge = @"window.__didinSubs = window.__didinSubs || {
            s: { videoId: null, want: null, reported: null, dotnet: null, bound: null },
            apply: function () {
                var S = window.__didinSubs, v = document.getElementById(S.s.videoId); if (!v) return;
                for (var i = 0; i < v.textTracks.length; i++) {
                    var t = v.textTracks[i], m = (S.s.want && t.id === S.s.want) ? 'showing' : 'disabled';
                    if (t.mode !== m) t.mode = m;
                }
            },
            init: function (videoId, dotnet, want) {
                var S = window.__didinSubs; S.s.videoId = videoId; S.s.dotnet = dotnet; S.s.want = want || null; S.s.reported = S.s.want;
                var v = document.getElementById(videoId); if (!v) return;
                if (S.s.bound !== v) {
                    S.s.bound = v;
                    v.textTracks.addEventListener('change', function () {
                        var on = [];
                        for (var i = 0; i < v.textTracks.length; i++) { if (v.textTracks[i].mode === 'showing') on.push(v.textTracks[i]); }
                        // Two at once is never a person's choice (the browser's own menu shows one): WebView2 switches a
                        // track on by itself as it arrives during playback. Put the chosen one back.
                        if (on.length > 1) { S.apply(); return; }
                        var showing = on.length ? (on[0].id || null) : null;
                        if (showing === S.s.reported) return;
                        S.s.reported = showing; S.s.want = showing;
                        if (S.s.dotnet) S.s.dotnet.invokeMethodAsync('OnSubtitleChanged', showing).catch(function () {});
                    });
                    v.textTracks.addEventListener('addtrack', function () { S.apply(); });
                }
                S.apply();
            },
            set: function (id) { var S = window.__didinSubs; S.s.want = id || null; S.s.reported = S.s.want; S.apply(); },
            release: function () { window.__didinSubs.s.dotnet = null; }
        };
        window.__didinFocus = window.__didinFocus || function (selector) {
            var el = document.querySelector(selector); if (el) el.focus({ preventScroll: true });
        };";

        /// <summary>Calls the player's OnVideoEnded when the video plays to its end (the next part or episode follows).</summary>
        public async Task OnEndedAsync<T>(string videoId, DotNetObjectReference<T> player) where T : class
        {
            await _js.InvokeVoidAsync("eval",
                "window.__didinOnEnded = function (id, dotnet) { var v = document.getElementById(id); if (!v) return; " +
                "v.addEventListener('ended', function () { dotnet.invokeMethodAsync('OnVideoEnded'); }, { once: true }); };");
            await _js.InvokeVoidAsync("__didinOnEnded", videoId, player);
        }

        public async Task InitSubtitlesAsync<T>(string videoId, DotNetObjectReference<T> player, string trackId) where T : class
        {
            try
            {
                await _js.InvokeVoidAsync("eval", SubtitleBridge);
                await _js.InvokeVoidAsync("__didinSubs.init", videoId, player, trackId);
            }
            catch { /* subtitles stay off; the video still plays */ }
        }

        public async Task SetSubtitleAsync(string trackId)
        {
            try { await _js.InvokeVoidAsync("__didinSubs.set", trackId); }
            catch { }
        }

        public async Task ReleaseSubtitlesAsync()
        {
            try { await _js.InvokeVoidAsync("eval", "window.__didinSubs && window.__didinSubs.release()"); }
            catch { /* circuit gone */ }
        }

        /// <summary>Moves keyboard focus (a TV remote's too) to the first element matching the selector.</summary>
        public async Task FocusAsync(string selector)
        {
            try
            {
                await _js.InvokeVoidAsync("eval", SubtitleBridge);
                await _js.InvokeVoidAsync("__didinFocus", selector);
            }
            catch { }
        }

        public async Task<string> LoadFiltersAsync()
        {
            try { return await _js.InvokeAsync<string>("eval", "(() => { try { return window.localStorage.getItem('didin.filters'); } catch (e) { return null; } })()"); }
            catch { return null; }
        }

        public async Task SaveFiltersAsync(string json)
        {
            try
            {
                var payload = System.Text.Encodings.Web.JavaScriptEncoder.Default.Encode(json);
                await _js.InvokeVoidAsync("eval",
                    $"try {{ window.localStorage.setItem('didin.filters', \"{payload}\"); }} catch (e) {{ }}");
            }
            catch { }
        }
    }
}
