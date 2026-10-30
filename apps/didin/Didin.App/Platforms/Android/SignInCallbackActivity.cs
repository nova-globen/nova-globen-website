using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Didin.App;

// The site's sign-in sends the browser to se.novaglobenab.didin:/signin?code=...; this activity catches it and hands
// it to the waiting sign-in (Services/BrowserSignIn).
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(new[] { Intent.ActionView }, Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "se.novaglobenab.didin")]
public class SignInCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
