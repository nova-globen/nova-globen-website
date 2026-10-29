using System.Reflection;

namespace Didin.App;

/// <summary>
/// Which Didin site the app talks to: its folder on a host, which is both the API's base and, from Stomme 10.76, the
/// sign-in issuer. It is set when the app is built (`-p:DidinSite=https://<host>/didin`, Didin.App.csproj):
/// a release build defaults to nova-globen.se's, whose public catalogue a guest can browse; a debug build to a local
/// host's (http://localhost:5100/didin - on an Android emulator or a USB-connected device through `adb reverse tcp:5100
/// tcp:5100`; the WebView treats localhost as secure, so posters and video from it are not blocked as mixed content).
/// DIDIN_SERVER overrides it on Windows (Android has no such variable).
/// </summary>
public static class AppConfig
{
    public static string SiteUrl { get; } =
        (Environment.GetEnvironmentVariable("DIDIN_SERVER")
         ?? typeof(AppConfig).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault(a => a.Key == "DidinSite")?.Value
         ?? "https://nova-globen.se/didin").TrimEnd('/');

    /// <summary>The host the site is on ("https://nova-globen.se"): the API answers poster addresses under it.</summary>
    public static string Origin => new Uri(SiteUrl).GetLeftPart(UriPartial.Authority);
}
