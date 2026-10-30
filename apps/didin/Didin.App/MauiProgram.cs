using Didin.App.Services;
using Didin.Module.Media.Services;
using Microsoft.Extensions.Logging;
using Microsoft.FluentUI.AspNetCore.Components;

namespace Didin.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // the UI is English: "7.8", not the device's own "7,8" (as on the site)
        var english = new System.Globalization.CultureInfo("en-US");
        System.Globalization.CultureInfo.DefaultThreadCurrentCulture = english;
        System.Globalization.CultureInfo.DefaultThreadCurrentUICulture = english;

        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddFluentUIComponents();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // The module's own UI (Modules/Didin/Ui) against the Didin site's API: the site's folder is the API's base, the
        // host is where its poster and subtitle addresses resolve. Subtitles come through the client with the token
        // (ApiBase set), and a signed-in person's video plays from a sealed ticket (TicketPlayback). A guest until
        // signed in at the site's own authority (Services/SignIn.cs).
        var options = new MediaOptions
        {
            SiteBase = AppConfig.SiteUrl, Origin = AppConfig.Origin, ApiBase = AppConfig.SiteUrl, TicketPlayback = true, Guest = true,
        };
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(120) });
        builder.Services.AddSingleton<IBrowserSignIn, BrowserSignIn>();
        builder.Services.AddSingleton<ITokenStore, TokenStore>();
        builder.Services.AddSingleton<ISignIn, SiteSignIn>();
        builder.Services.AddScoped<ICatalogClient, ApiCatalogClient>();
        builder.Services.AddScoped<MediaState>();
#if ANDROID
        // cast to a TV through the native Cast framework; an Android TV itself does not cast
        builder.Services.AddSingleton<ICastDevice, Didin.App.Platforms.Android.AndroidCastDevice>();
#else
        builder.Services.AddSingleton<ICastDevice, NoCastDevice>();
#endif

        return builder.Build();
    }
}
