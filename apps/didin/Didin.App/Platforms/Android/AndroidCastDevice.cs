using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.Res;
using Android.Gms.Cast;
using Android.Gms.Cast.Framework;
using Android.Gms.Cast.Framework.Media;
using Android.Gms.Common;
using Android.Runtime;
using AndroidX.MediaRouter.App;
using AndroidX.MediaRouter.Media;
using Didin.Module.Media.Services;
using Didin.Shared.Models;

namespace Didin.App.Platforms.Android;

/// <summary>Google's Cast framework reads its options here (the manifest names this class): the Default Media Receiver.</summary>
[Register("se.novaglobenab.didin.CastOptionsProvider")]
public class CastOptionsProvider : Java.Lang.Object, IOptionsProvider
{
    public CastOptions GetCastOptions(Context context) =>
        new CastOptions.Builder().SetReceiverApplicationId(CastMediaControlIntent.DefaultMediaReceiverApplicationId).Build();

    public IList<SessionProvider> GetAdditionalSessionProviders(Context context) => null;
}

/// <summary>
/// Cast to TV from the Android app: the native Cast framework, Google's Default Media Receiver on the TV, the
/// system's device picker. The page's WebView has no Cast API, so the player talks to this through ICastDevice. A phone
/// without Google Play services, and an Android TV (it is the screen), never report a device, so no button shows.
/// </summary>
public class AndroidCastDevice : ICastDevice
{
    private CastContext _context;
    private MediaRouter _router;
    private RemoteMediaClient _client;
    private readonly MediaCallback _mediaCallback;
    private readonly ProgressListener _progress;
    private string _file;
    private bool _ended;
    private Func<CastSession, Task> _pending;    // a load waiting for the device the user is picking

    public AndroidCastDevice()
    {
        _mediaCallback = new MediaCallback(this);
        _progress = new ProgressListener(this);
    }

    public CastStatus Status { get; private set; } = new();
    public event Action Changed;

    public Task InitAsync() => MainThread.InvokeOnMainThreadAsync(Init);

    private void Init()
    {
        if (_context != null) return;
        var activity = Platform.CurrentActivity;
        if (activity == null || IsTv(activity)) return;
        try
        {
            if (GoogleApiAvailability.Instance.IsGooglePlayServicesAvailable(activity) != ConnectionResult.Success) return;
#pragma warning disable CS0618 // the synchronous GetSharedInstance: called on the main thread, once
            _context = CastContext.GetSharedInstance(activity);
#pragma warning restore CS0618
        }
        catch (Exception) { _context = null; return; }

        _context.AddCastStateListener(new StateListener(this));
        _context.SessionManager.AddSessionManagerListener(new SessionListener(this), Java.Lang.Class.FromType(typeof(CastSession)));
        // look for devices actively while the app runs, so the button shows as soon as the player opens
        _router = MediaRouter.GetInstance(activity);
        _router.AddCallback(_context.MergedSelector, new RouterCallback(), MediaRouter.CallbackFlagRequestDiscovery);
        Attach(_context.SessionManager.CurrentCastSession);
        Update();
    }

    private static bool IsTv(Context context) =>
        (context.Resources?.Configuration?.UiMode & UiMode.TypeMask) == UiMode.TypeTelevision;

    public Task StartAsync(string fileToken, CastMedia media, string title, double position, int? track) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            Init();
            if (_context == null) return;
            Func<CastSession, Task> load = session => Load(session, fileToken, media, title, position, track);
            var current = _context.SessionManager.CurrentCastSession;
            if (current != null && current.IsConnected) { _ = load(current); return; }
            _pending = load;
            var activity = Platform.CurrentActivity;
            if (activity == null) return;
            new MediaRouteChooserDialog(activity) { RouteSelector = _context.MergedSelector }.Show();
        });

    private Task Load(CastSession session, string fileToken, CastMedia media, string title, double position, int? track)
    {
        Attach(session);
        if (_client == null) return Task.CompletedTask;
        var metadata = new MediaMetadata(MediaMetadata.MediaTypeMovie);
        metadata.PutString(MediaMetadata.KeyTitle, title ?? "");
        var tracks = (media.Subtitles ?? new()).Select((s, i) =>
            new MediaTrack.Builder(i + 1, MediaTrack.TypeText)
                .SetName(s.Label).SetSubtype(MediaTrack.SubtypeSubtitles).SetContentId(Absolute(s.Url))
                .SetContentType("text/vtt").SetLanguage(s.Lang).Build()).ToList();
        var info = new MediaInfo.Builder(Absolute(media.Url))
            .SetStreamType(MediaInfo.StreamTypeBuffered)
            .SetContentType(media.ContentType)
            .SetMetadata(metadata)
            .SetMediaTracks(tracks)
            .Build();
        var request = new MediaLoadRequestData.Builder()
            .SetMediaInfo(info)
            .SetAutoplay(Java.Lang.Boolean.True)
            .SetCurrentTime((long)(Math.Max(0, position) * 1000))
            .SetActiveTrackIds(track is int t ? new long[] { t + 1 } : Array.Empty<long>())
            .Build();
        _file = fileToken;
        _ended = false;
        _client.Load(request);
        Update();
        return Task.CompletedTask;
    }

    private static string Absolute(string url) => url.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? url : AppConfig.Origin + url;   // the API answers addresses relative to its host

    private void Attach(CastSession session)
    {
        var client = session?.RemoteMediaClient;
        if (client == _client) return;
        if (_client != null)
        {
            _client.UnregisterCallback(_mediaCallback);
            _client.RemoveProgressListener(_progress);
        }
        _client = client;
        if (_client != null)
        {
            _client.RegisterCallback(_mediaCallback);
            _client.AddProgressListener(_progress, 1000);
        }
    }

    public Task PlayPauseAsync() => MainThread.InvokeOnMainThreadAsync(() => { _client?.TogglePlayback(); });

    public Task SeekAsync(double position) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        _client?.Seek(new MediaSeekOptions.Builder().SetPosition((long)(Math.Max(0, position) * 1000)).Build());
    });

    public Task SetTrackAsync(int? track) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        _client?.SetActiveMediaTracks(track is int t ? new long[] { t + 1 } : Array.Empty<long>());
    });

    public Task StopAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
        _file = null;
        _context?.SessionManager.EndCurrentSession(true);
        Update();
    });

    private void Update()
    {
        var session = _context?.SessionManager.CurrentCastSession;
        var connected = session != null && session.IsConnected;
        var status = _client?.MediaStatus;
        if (status != null && status.PlayerState == MediaStatus.PlayerStateIdle && status.IdleReason == MediaStatus.IdleReasonFinished)
        {
            _ended = true;
        }
        Status = new CastStatus
        {
            Available = _context != null && _context.CastState != CastStates.NoDevicesAvailable,
            Connected = connected,
            Device = session?.CastDevice?.FriendlyName,
            FileToken = connected && _client?.HasMediaSession == true ? _file : null,
            Position = (_client?.ApproximateStreamPosition ?? 0) / 1000.0,
            Duration = (_client?.StreamDuration ?? 0) / 1000.0,
            Paused = _client?.IsPaused ?? false,
            Ended = _ended,
        };
        Changed?.Invoke();
    }

    private void OnSessionStarted(CastSession session)
    {
        Attach(session);
        var pending = _pending;
        _pending = null;
        if (pending != null) _ = pending(session);
        Update();
    }

    private void OnSessionEnded()
    {
        _pending = null;
        _file = null;
        Attach(null);
        Update();
    }

    private sealed class StateListener : Java.Lang.Object, ICastStateListener
    {
        private readonly AndroidCastDevice _owner;
        public StateListener(AndroidCastDevice owner) => _owner = owner;
        public void OnCastStateChanged(int state) => _owner.Update();
    }

    private sealed class SessionListener : Java.Lang.Object, ISessionManagerListener
    {
        private readonly AndroidCastDevice _owner;
        public SessionListener(AndroidCastDevice owner) => _owner = owner;
        public void OnSessionStarted(Java.Lang.Object session, string sessionId) => _owner.OnSessionStarted(session as CastSession);
        public void OnSessionResumed(Java.Lang.Object session, bool wasSuspended) => _owner.OnSessionStarted(session as CastSession);
        public void OnSessionEnded(Java.Lang.Object session, int error) => _owner.OnSessionEnded();
        public void OnSessionStartFailed(Java.Lang.Object session, int error) => _owner.OnSessionEnded();
        public void OnSessionResumeFailed(Java.Lang.Object session, int error) => _owner.OnSessionEnded();
        public void OnSessionSuspended(Java.Lang.Object session, int reason) => _owner.Update();
        public void OnSessionEnding(Java.Lang.Object session) { }
        public void OnSessionResuming(Java.Lang.Object session, string sessionId) { }
        public void OnSessionStarting(Java.Lang.Object session) { }
    }

    private sealed class MediaCallback : RemoteMediaClient.Callback
    {
        private readonly AndroidCastDevice _owner;
        public MediaCallback(AndroidCastDevice owner) => _owner = owner;
        public override void OnStatusUpdated() => _owner.Update();
        public override void OnMetadataUpdated() => _owner.Update();
    }

    private sealed class ProgressListener : Java.Lang.Object, RemoteMediaClient.IProgressListener
    {
        private readonly AndroidCastDevice _owner;
        public ProgressListener(AndroidCastDevice owner) => _owner = owner;
        public void OnProgressUpdated(long progressMs, long durationMs) => _owner.Update();
    }

    private sealed class RouterCallback : MediaRouter.Callback { }
}
