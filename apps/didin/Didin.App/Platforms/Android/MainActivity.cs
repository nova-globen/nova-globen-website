using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Activity;

namespace Didin.App;

// MainLauncher puts the app in the phone's launcher; the leanback category puts it on an Android TV's home screen,
// with the banner from the manifest.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
[IntentFilter(new[] { Intent.ActionMain }, Categories = new[] { Intent.CategoryLeanbackLauncher })]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        // Back (a phone's gesture, a TV remote's button) closes what is open in the page; from the catalog it leaves.
        // Android 13+ routes Back through this dispatcher - the callback added last wins over MAUI's own.
        OnBackPressedDispatcher.AddCallback(this, new BackCallback(this));
    }

    private sealed class BackCallback : OnBackPressedCallback
    {
        private readonly Activity _activity;

        public BackCallback(Activity activity) : base(true) => _activity = activity;

        public override void HandleOnBackPressed()
        {
            if (!AndroidWebView.OnBack(_activity)) _activity.MoveTaskToBack(true);
        }
    }
}
