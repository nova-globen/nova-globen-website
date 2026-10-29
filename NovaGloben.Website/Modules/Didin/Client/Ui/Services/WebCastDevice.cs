using System;
using System.Threading.Tasks;
using Didin.Shared.Models;
using Microsoft.JSInterop;

namespace Didin.Module.Media.Services
{
    /// <summary>
    /// Cast to TV on the website: Google's Cast SDK for Chrome (cast_sender.js, loaded on first use and only in
    /// a browser that has the Cast API), Google's Default Media Receiver on the TV. Like Interop, the page half is an
    /// eval'd snippet - the host includes no module script. A browser without the Cast API, or with no device
    /// on its network, never reports one available, so the player shows no button.
    /// </summary>
    public class WebCastDevice : ICastDevice, IDisposable
    {
        private readonly IJSRuntime _js;
        private DotNetObjectReference<WebCastDevice> _self;

        public WebCastDevice(IJSRuntime js) => _js = js;

        public CastStatus Status { get; private set; } = new();
        public event Action Changed;

        private const string Bridge = @"window.__didinCast = window.__didinCast || (function () {
            var S = { dotnet: null, ready: false, ctx: null, player: null, ctrl: null, available: false, file: null, ended: false, lastTime: 0 };
            var abs = function (u) { return new URL(u, window.location.origin).href; };
            var report = function () {
                if (!S.dotnet) return;
                var p = S.player, s = S.ctx && S.ctx.getCurrentSession();
                S.dotnet.invokeMethodAsync('OnCastStatus', {
                    available: S.available, connected: !!(p && p.isConnected),
                    device: s ? s.getCastDevice().friendlyName : null, fileToken: p && p.isMediaLoaded ? S.file : null,
                    position: p ? p.currentTime : 0, duration: p ? p.duration : 0, paused: p ? p.isPaused : false, ended: S.ended
                }).catch(function () {});
            };
            var setup = function () {
                if (S.ready) return;
                var ctx = cast.framework.CastContext.getInstance();
                ctx.setOptions({ receiverApplicationId: chrome.cast.media.DEFAULT_MEDIA_RECEIVER_APP_ID, autoJoinPolicy: chrome.cast.AutoJoinPolicy.ORIGIN_SCOPED });
                S.ctx = ctx; S.player = new cast.framework.RemotePlayer(); S.ctrl = new cast.framework.RemotePlayerController(S.player);
                ctx.addEventListener(cast.framework.CastContextEventType.CAST_STATE_CHANGED, function (e) {
                    S.available = e.castState !== cast.framework.CastState.NO_DEVICES_AVAILABLE; report();
                });
                S.ctrl.addEventListener(cast.framework.RemotePlayerEventType.ANY_CHANGE, function (e) {
                    if (e.field === 'playerState' && e.value === chrome.cast.media.PlayerState.IDLE) {
                        var m = S.ctx.getCurrentSession() && S.ctx.getCurrentSession().getMediaSession();
                        if (m && m.idleReason === chrome.cast.media.IdleReason.FINISHED) S.ended = true;
                    }
                    if (e.field === 'isConnected' && !e.value) S.file = null;
                    // the clock ticks every second: tell the page every 5 s of it, everything else at once
                    if (e.field === 'currentTime' && Math.abs(e.value - S.lastTime) < 5) return;
                    if (e.field === 'currentTime') S.lastTime = e.value;
                    report();
                });
                S.available = ctx.getCastState() !== cast.framework.CastState.NO_DEVICES_AVAILABLE;
                S.ready = true; report();
            };
            var load = function (fileToken, media, title, position, track) {
                var info = new chrome.cast.media.MediaInfo(abs(media.url), media.contentType);
                info.streamType = chrome.cast.media.StreamType.BUFFERED;
                info.metadata = new chrome.cast.media.MovieMediaMetadata(); info.metadata.title = title;
                info.tracks = (media.subtitles || []).map(function (s, i) {
                    var t = new chrome.cast.media.Track(i + 1, chrome.cast.media.TrackType.TEXT);
                    t.trackContentId = abs(s.url); t.trackContentType = 'text/vtt';
                    t.subtype = chrome.cast.media.TextTrackType.SUBTITLES; t.name = s.label; t.language = s.lang;
                    return t;
                });
                var req = new chrome.cast.media.LoadRequest(info);
                req.currentTime = position || 0; req.autoplay = true;
                if (track !== null && track !== undefined) req.activeTrackIds = [track + 1];
                S.file = fileToken; S.ended = false;
                return S.ctx.getCurrentSession().loadMedia(req).then(report);
            };
            return {
                init: function (dotnet) {
                    S.dotnet = dotnet;
                    if (S.ready) { report(); return; }
                    if (!window.chrome || window.__didinCastLoading) return;   // not Chrome: no Cast API
                    if (window.cast && window.cast.framework) { setup(); return; }
                    window.__didinCastLoading = true;
                    window.__onGCastApiAvailable = function (ok) { if (ok) setup(); };
                    var s = document.createElement('script');
                    s.src = 'https://www.gstatic.com/cv/js/sender/v1/cast_sender.js?loadCastFramework=1';
                    document.head.appendChild(s);
                },
                start: function (fileToken, media, title, position, track) {
                    if (!S.ready) return Promise.resolve();
                    if (S.ctx.getCurrentSession()) return load(fileToken, media, title, position, track);
                    // the device picker; closing it without a choice rejects, which is no error
                    return S.ctx.requestSession().then(function () { return load(fileToken, media, title, position, track); }, function () {});
                },
                playPause: function () { if (S.ctrl) S.ctrl.playOrPause(); },
                seek: function (t) { if (S.player && S.player.canSeek) { S.player.currentTime = t; S.ctrl.seek(); } },
                track: function (track) {
                    var m = S.ctx && S.ctx.getCurrentSession() && S.ctx.getCurrentSession().getMediaSession(); if (!m) return;
                    m.editTracksInfo(new chrome.cast.media.EditTracksInfoRequest(track === null || track === undefined ? [] : [track + 1]), function () {}, function () {});
                },
                stop: function () { S.file = null; if (S.ctx && S.ctx.getCurrentSession()) S.ctx.endCurrentSession(true); report(); }
            };
        })();";

        public async Task InitAsync()
        {
            try
            {
                _self ??= DotNetObjectReference.Create(this);
                await _js.InvokeVoidAsync("eval", Bridge);
                await _js.InvokeVoidAsync("__didinCast.init", _self);
            }
            catch { /* no cast button; the player is unaffected */ }
        }

        public async Task StartAsync(string fileToken, CastMedia media, string title, double position, int? track)
        {
            try { await _js.InvokeVoidAsync("__didinCast.start", fileToken, media, title, position, track); }
            catch { }
        }

        public Task PlayPauseAsync() => Call("__didinCast.playPause");
        public Task SeekAsync(double position) => Call("__didinCast.seek", position);
        public Task SetTrackAsync(int? track) => Call("__didinCast.track", track);
        public Task StopAsync() => Call("__didinCast.stop");

        private async Task Call(string fn, params object[] args)
        {
            try { await _js.InvokeVoidAsync(fn, args); }
            catch { /* the device or the circuit is gone */ }
        }

        [JSInvokable]
        public void OnCastStatus(CastStatus status)
        {
            Status = status ?? new CastStatus();
            Changed?.Invoke();
        }

        // the page's reports to a disposed reference fail quietly there (they are caught)
        public void Dispose() => _self?.Dispose();
    }
}
