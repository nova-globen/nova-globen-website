using Android.App;
using Android.Views;
using Android.Webkit;
using Android.Widget;
using AndroidX.Core.View;
using View = Android.Views.View;
using WebView = Android.Webkit.WebView;

namespace Didin.App;

/// <summary>
/// What the Android WebView needs beyond MAUI's defaults: fullscreen video (a WebView shows an element in
/// fullscreen only through its chrome client), playback that may start without a tap (the next part plays on by
/// itself), and the Back button (a phone's, or a TV remote's) closing what is open instead of leaving the app.
/// </summary>
public static class AndroidWebView
{
    private static WebView _view;

    public static void Apply(WebView view)
    {
        _view = view;
        view.Settings.MediaPlaybackRequiresUserGesture = false;
        if (Platform.CurrentActivity is Activity activity)
        {
            view.SetWebChromeClient(new FullscreenChromeClient(activity));
        }
    }

    // The open view's own close or back control, innermost first. "home" when nothing is open: Back then leaves.
    private const string BackScript = @"(function () {
        var steps = [['#bin-link', '#bin-link .bin-link-close'], ['#bin-player-shell', '#bin-close'],
                     ['.bin-filters-open', '.bin-drawer-head .bin-icon-btn'], ['#bin-library', '#bin-library-back'],
                     ['.bin-details', '#bin-back']];
        for (var i = 0; i < steps.length; i++) {
            if (document.querySelector(steps[i][0])) {
                var close = document.querySelector(steps[i][1]);
                if (close) { close.click(); return 'back'; }
            }
        }
        return 'home';
    })()";

    /// <summary>Back pressed: the page closes what is open; with nothing open, the app goes to the background.</summary>
    public static bool OnBack(Activity activity)
    {
        if (_view == null) return false;
        _view.EvaluateJavascript(BackScript, new BackResult(activity));
        return true;
    }

    private sealed class BackResult : Java.Lang.Object, IValueCallback
    {
        private readonly Activity _activity;
        public BackResult(Activity activity) => _activity = activity;

        public void OnReceiveValue(Java.Lang.Object value)
        {
            if (value?.ToString()?.Contains("home") == true) _activity.MoveTaskToBack(true);
        }
    }

    /// <summary>Element fullscreen (the player's wrapper): the WebView hands over a view to show over everything.</summary>
    private sealed class FullscreenChromeClient : WebChromeClient
    {
        private readonly Activity _activity;
        private View _custom;
        private ICustomViewCallback _callback;

        public FullscreenChromeClient(Activity activity) => _activity = activity;

        public override void OnShowCustomView(View view, ICustomViewCallback callback)
        {
            if (_custom != null)
            {
                callback?.OnCustomViewHidden();
                return;
            }
            _custom = view;
            _callback = callback;
            view.KeepScreenOn = true;   // no screensaver while a film plays
            var decor = (FrameLayout)_activity.Window!.DecorView;
            decor.AddView(view, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));
            var bars = WindowCompat.GetInsetsController(_activity.Window, decor);
            bars.SystemBarsBehavior = WindowInsetsControllerCompat.BehaviorShowTransientBarsBySwipe;
            bars.Hide(WindowInsetsCompat.Type.SystemBars());
        }

        public override void OnHideCustomView()
        {
            if (_custom == null) return;
            var decor = (FrameLayout)_activity.Window!.DecorView;
            decor.RemoveView(_custom);
            WindowCompat.GetInsetsController(_activity.Window, decor).Show(WindowInsetsCompat.Type.SystemBars());
            _custom = null;
            _callback?.OnCustomViewHidden();
            _callback = null;
        }
    }
}
