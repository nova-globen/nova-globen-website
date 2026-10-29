using Microsoft.AspNetCore.Components.WebView;

namespace Didin.App;

public partial class MainPage : ContentPage
{
	public MainPage()
	{
		InitializeComponent();
	}

	/// <summary>
	/// After MAUI has set the platform WebView up - the place to change its settings, so MAUI does not overwrite them
	/// (fullscreen video, playback without a tap, and a debug build's http server on Android).
	/// </summary>
	private void OnWebViewInitialized(object sender, BlazorWebViewInitializedEventArgs e)
	{
#if ANDROID
		AndroidWebView.Apply(e.WebView);
#endif
	}
}
