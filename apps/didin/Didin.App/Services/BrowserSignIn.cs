
namespace Didin.App.Services;

#if ANDROID
/// <summary>
/// Android: the site's sign-in page in a Custom Tab, back to the app through its private-use scheme
/// (se.novaglobenab.didin:/signin, caught by Platforms/Android/SignInCallbackActivity).
/// </summary>
public sealed class BrowserSignIn : IBrowserSignIn
{
    public const string RedirectUri = "se.novaglobenab.didin:/signin";

    public async Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(Func<string, string> authorizeUrl, CancellationToken ct)
    {
        try
        {
            var result = await WebAuthenticator.Default.AuthenticateAsync(new WebAuthenticatorOptions
            {
                Url = new Uri(authorizeUrl(RedirectUri)),
                CallbackUrl = new Uri(RedirectUri),
            });
            return result?.Properties == null ? null : new Dictionary<string, string>(result.Properties);
        }
        catch (TaskCanceledException)
        {
            return null;   // the person closed the tab
        }
    }
}
#else
/// <summary>
/// Windows: the site's sign-in page in the system browser, back to a one-shot loopback listener (LoopbackRedirect).
/// </summary>
public sealed class BrowserSignIn : IBrowserSignIn
{
    public Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(Func<string, string> authorizeUrl, CancellationToken ct) =>
        LoopbackRedirect.ReceiveAsync(authorizeUrl, url => Launcher.Default.OpenAsync(new Uri(url)), ct);
}
#endif

#if ANDROID
/// <summary>Android: the platform's secure storage (the Keystore).</summary>
public sealed class TokenStore : ITokenStore
{
    private const string Key = "didin.refresh";
    public Task<string> GetAsync() => SecureStorage.Default.GetAsync(Key);
    public Task SetAsync(string refreshToken) => SecureStorage.Default.SetAsync(Key, refreshToken);
    public void Remove() => SecureStorage.Default.Remove(Key);
}
#else
/// <summary>
/// Windows: a file under the user's local app data, encrypted for this Windows user (DPAPI). MAUI's SecureStorage needs
/// a packaged app, and this one is unpackaged.
/// </summary>
public sealed class TokenStore : ITokenStore
{
    private static readonly string Path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Didin", "signin.bin");

    public async Task<string> GetAsync()
    {
        if (!File.Exists(Path)) return null;
        var provider = new Windows.Security.Cryptography.DataProtection.DataProtectionProvider();
        var plain = await provider.UnprotectAsync(Windows.Security.Cryptography.CryptographicBuffer.CreateFromByteArray(await File.ReadAllBytesAsync(Path)));
        return Windows.Security.Cryptography.CryptographicBuffer.ConvertBinaryToString(Windows.Security.Cryptography.BinaryStringEncoding.Utf8, plain);
    }

    public async Task SetAsync(string refreshToken)
    {
        var provider = new Windows.Security.Cryptography.DataProtection.DataProtectionProvider("LOCAL=user");
        var sealedBuffer = await provider.ProtectAsync(Windows.Security.Cryptography.CryptographicBuffer.ConvertStringToBinary(refreshToken, Windows.Security.Cryptography.BinaryStringEncoding.Utf8));
        Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(sealedBuffer, out var bytes);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
        await File.WriteAllBytesAsync(Path, bytes);
    }

    public void Remove()
    {
        if (File.Exists(Path)) File.Delete(Path);
    }
}
#endif
