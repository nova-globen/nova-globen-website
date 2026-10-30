using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Didin.App.Services;

/// <summary>
/// Signing the app in to its Didin site, and the access token that sign-in gives it. The site is its own issuer
/// (https://&lt;host&gt;/didin, Stomme 10.76): authorization code with PKCE in the system browser, the app a public
/// client of that site only (didin-app, registered by the module). A TV, which may have no browser, signs in through the
/// person's phone instead (the device authorization grant, RFC 8628, Stomme 10.77). There is deliberately no password
/// sign-in.
/// </summary>
public interface ISignIn
{
    /// <summary>The signed-in person's name, or null for a guest.</summary>
    string UserName { get; }

    /// <summary>True while a refresh token is held.</summary>
    bool IsSignedIn { get; }

    /// <summary>Picks up the last session (its refresh token) when the app starts; quietly a guest when it cannot.</summary>
    Task RestoreAsync(CancellationToken ct = default);

    /// <summary>A current access token, renewed when it is about to expire; null for a guest.</summary>
    Task<string> GetAccessTokenAsync(CancellationToken ct = default);

    /// <summary>Signs in; null when it did, else why not, for the person.</summary>
    Task<string> SignInAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts signing in through the person's phone: the code and the address to show. Null with the reason for the
    /// person when it cannot.
    /// </summary>
    Task<(DeviceSignIn Start, string Problem)> StartPhoneSignInAsync(CancellationToken ct = default);

    /// <summary>
    /// Waits for the phone's answer to <paramref name="start"/>, asking the site as often as it allows; null when
    /// signed in, else why not, for the person. Cancelling <paramref name="ct"/> stops the waiting.
    /// </summary>
    Task<string> FinishPhoneSignInAsync(DeviceSignIn start, CancellationToken ct = default);

    /// <summary>Signs out: revokes the tokens at the site and forgets them.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>
/// A sign-in waiting for the person's phone: the code they enter, the site's address to enter it at, the same address
/// with the code in it (for the QR code), and until when it holds. The device code stays with the app.
/// </summary>
public sealed record DeviceSignIn(string UserCode, string VerificationUri, string VerificationUriComplete, DateTimeOffset Until)
{
    internal string DeviceCode { get; init; }
    internal int Interval { get; init; }
}

/// <summary>
/// The browser half of signing in, which differs per platform: it opens the site's sign-in page and hands back the
/// parameters the site redirected to the app with.
/// </summary>
public interface IBrowserSignIn
{
    /// <summary>
    /// Opens the address <paramref name="authorizeUrl"/> makes for the redirect address this platform listens on, and
    /// answers the redirect's query parameters; null when the person closed the browser.
    /// </summary>
    Task<IReadOnlyDictionary<string, string>> AuthenticateAsync(Func<string, string> authorizeUrl, CancellationToken ct);
}

/// <summary>Where the refresh token is kept between runs, encrypted by the platform.</summary>
public interface ITokenStore
{
    Task<string> GetAsync();
    Task SetAsync(string refreshToken);
    void Remove();
}

/// <summary>
/// OpenID Connect against the Didin site's own authority: discovery at {site}/.well-known/openid-configuration, the code
/// flow with PKCE (S256), state and the issuer checked on the way back, the refresh token kept by the platform and
/// rotated as the site issues new ones, and both tokens revoked at sign-out (RFC 7009). The access token stays in memory
/// and goes only into an Authorization header.
/// </summary>
public sealed class SiteSignIn(HttpClient http, IBrowserSignIn browser, ITokenStore store) : ISignIn
{
    public const string ClientId = "didin-app";
    private const string Scope = "openid profile offline_access api";
    internal const string Unreachable = "Signing in could not reach the site. Check the connection and try again.";

    private Discovery _discovery;
    private string _access;
    private DateTimeOffset _accessUntil;
    private string _refresh;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public string UserName { get; private set; }
    public bool IsSignedIn => _refresh != null;

    public async Task RestoreAsync(CancellationToken ct = default)
    {
        // read the sign-in configuration in the background, so a tap on Sign in does not wait for a cold connection
        _ = DiscoverAsync(CancellationToken.None).ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

        string saved;
        try { saved = await store.GetAsync(); }
        catch { saved = null; }   // an unreadable store is a guest, not a crash
        if (string.IsNullOrEmpty(saved)) return;

        _refresh = saved;
        try
        {
            if (await RefreshAsync(ct)) await LoadNameAsync(ct);
        }
        catch (Exception)
        {
            // offline, or the site not answering as it should: keep the refresh token and try again on the first call
        }
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct = default)
    {
        if (_access != null && _accessUntil > DateTimeOffset.UtcNow.AddSeconds(60)) return _access;
        if (_refresh == null) return null;
        await _gate.WaitAsync(ct);
        try
        {
            if (_access == null || _accessUntil <= DateTimeOffset.UtcNow.AddSeconds(60))
            {
                await RefreshAsync(ct);
            }
            return _access;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<string> SignInAsync(CancellationToken ct = default)
    {
        Discovery discovery;
        try { discovery = await DiscoverAsync(ct); }
        catch (Exception) { return Unreachable; }

        var verifier = RandomToken();
        var state = RandomToken();
        var nonce = RandomToken();
        string redirectUri = null;
        IReadOnlyDictionary<string, string> reply;
        try
        {
            reply = await browser.AuthenticateAsync(redirect =>
            {
                redirectUri = redirect;
                return AuthorizeUrl(discovery.AuthorizationEndpoint, redirect, Challenge(verifier), state, nonce);
            }, ct);
        }
        catch (Exception)
        {
            return "Signing in did not finish.";
        }

        var problem = CheckReply(reply, state, discovery.Issuer);
        if (problem != null) return problem;

        try
        {
            var tokens = await PostTokenAsync(discovery, new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = reply["code"],
                ["redirect_uri"] = redirectUri,
                ["client_id"] = ClientId,
                ["code_verifier"] = verifier,
            }, ct);
            if (tokens?.AccessToken == null) return "The site did not accept the sign-in.";
            await KeepAsync(tokens);
        }
        catch (Exception)
        {
            return Unreachable;
        }

        try { await LoadNameAsync(ct); }
        catch (Exception) { /* signed in; the name shows after the next start */ }
        return null;
    }

    public async Task<(DeviceSignIn Start, string Problem)> StartPhoneSignInAsync(CancellationToken ct = default)
    {
        Discovery discovery;
        try { discovery = await DiscoverAsync(ct); }
        catch (Exception) { return (null, Unreachable); }
        if (discovery.DeviceAuthorizationEndpoint == null) return (null, "This site does not offer signing in with a phone yet.");

        DeviceResponse reply;
        try
        {
            using var response = await http.PostAsync(discovery.DeviceAuthorizationEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["scope"] = Scope,
            }), ct);
            if (!response.IsSuccessStatusCode) return (null, "The site did not start a sign-in with a phone.");
            reply = await response.Content.ReadFromJsonAsync<DeviceResponse>(ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return (null, Unreachable);
        }

        // the person is sent to these addresses, so they must be the site's own
        if (string.IsNullOrEmpty(reply?.DeviceCode) || string.IsNullOrEmpty(reply.UserCode) || !OnSite(reply.VerificationUri))
        {
            return (null, "The site's answer was not one the app can use.");
        }
        return (new DeviceSignIn(reply.UserCode, reply.VerificationUri,
            OnSite(reply.VerificationUriComplete) ? reply.VerificationUriComplete : reply.VerificationUri,
            DateTimeOffset.UtcNow.AddSeconds(reply.ExpiresIn > 0 ? reply.ExpiresIn : 600))
        {
            DeviceCode = reply.DeviceCode,
            Interval = reply.Interval > 0 ? reply.Interval : 5,
        }, null);
    }

    public async Task<string> FinishPhoneSignInAsync(DeviceSignIn start, CancellationToken ct = default)
    {
        Discovery discovery;
        try { discovery = await DiscoverAsync(ct); }
        catch (Exception) when (!ct.IsCancellationRequested) { return Unreachable; }
        var interval = start.Interval;
        while (DateTimeOffset.UtcNow < start.Until)
        {
            await Task.Delay(TimeSpan.FromSeconds(interval), ct);
            string error;
            TokenResponse tokens;
            try
            {
                using var response = await http.PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = DeviceCodeGrant,
                    ["device_code"] = start.DeviceCode,
                    ["client_id"] = ClientId,
                }), ct);
                if (response.IsSuccessStatusCode)
                {
                    tokens = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
                    error = tokens?.AccessToken == null ? "invalid_response" : null;
                }
                else
                {
                    tokens = null;
                    error = (await ReadErrorAsync(response, ct)) ?? "unknown";
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                continue;   // a dropped connection: ask again at the next turn while the code holds
            }

            switch (error)
            {
                case null:
                    await KeepAsync(tokens);
                    try { await LoadNameAsync(ct); }
                    catch (Exception) when (!ct.IsCancellationRequested) { /* signed in; the name shows after the next start */ }
                    return null;
                case "authorization_pending":
                    break;
                case "slow_down":
                    interval += 5;   // RFC 8628 3.5
                    break;
                case "access_denied":
                    return "Signing in was declined on the phone.";
                case "expired_token":
                    return ExpiredCode;
                default:
                    return $"The site refused the sign-in ({error}).";
            }
        }
        return ExpiredCode;
    }

    internal const string DeviceCodeGrant = "urn:ietf:params:oauth:grant-type:device_code";
    internal const string ExpiredCode = "The code expired before the phone approved it. Try again for a new one.";

    /// <summary>True for an address on the Didin site's own host.</summary>
    internal static bool OnSite(string address) =>
        Uri.TryCreate(address, UriKind.Absolute, out var uri) && Uri.TryCreate(AppConfig.Origin, UriKind.Absolute, out var origin)
        && uri.Scheme == origin.Scheme && string.Equals(uri.Authority, origin.Authority, StringComparison.OrdinalIgnoreCase);

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try { return (await response.Content.ReadFromJsonAsync<ErrorResponse>(ct))?.Error; }
        catch (Exception) when (!ct.IsCancellationRequested) { return null; }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var refresh = _refresh;
        var access = _access;
        Forget();
        try
        {
            var discovery = await DiscoverAsync(ct);
            if (discovery.RevocationEndpoint != null)
            {
                if (refresh != null) await RevokeAsync(discovery, refresh, "refresh_token", ct);
                if (access != null) await RevokeAsync(discovery, access, "access_token", ct);
            }
        }
        catch (Exception)
        {
            // forgotten here either way; an unrevoked token expires on its own
        }
    }

    // ---- the protocol

    /// <summary>The authorization request: code flow, PKCE S256, state and nonce.</summary>
    internal static string AuthorizeUrl(string endpoint, string redirectUri, string challenge, string state, string nonce) =>
        endpoint + (endpoint.Contains('?') ? "&" : "?") + string.Join("&", new Dictionary<string, string>
        {
            ["client_id"] = ClientId,
            ["redirect_uri"] = redirectUri,
            ["response_type"] = "code",
            ["scope"] = Scope,
            ["code_challenge"] = challenge,
            ["code_challenge_method"] = "S256",
            ["state"] = state,
            ["nonce"] = nonce,
        }.Select(p => p.Key + "=" + Uri.EscapeDataString(p.Value)));

    /// <summary>
    /// What is wrong with the site's redirect back, or null when it carries a code for this request: the state must be
    /// the one sent, and an issuer it names must be the site's (RFC 9207, against a mix-up with another site).
    /// </summary>
    internal static string CheckReply(IReadOnlyDictionary<string, string> reply, string state, string issuer)
    {
        if (reply == null) return "Signing in was cancelled.";
        if (!reply.TryGetValue("state", out var returned) || returned != state) return "The sign-in reply did not belong to this request.";
        if (reply.TryGetValue("error", out var error)) return error == "access_denied" ? "Signing in was cancelled." : $"The site refused the sign-in ({error}).";
        if (reply.TryGetValue("iss", out var iss) && !SameIssuer(iss, issuer)) return "The sign-in reply came from another site.";
        return reply.TryGetValue("code", out var code) && !string.IsNullOrEmpty(code) ? null : "The site sent no sign-in code.";
    }

    internal static bool SameIssuer(string a, string b) =>
        string.Equals(a?.TrimEnd('/'), b?.TrimEnd('/'), StringComparison.Ordinal);

    internal static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    internal static string RandomToken() => Base64Url(RandomNumberGenerator.GetBytes(32));

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>How long one attempt to read the sign-in configuration may take, and the pauses before the retries.</summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(10);
    internal static readonly TimeSpan[] RetryPauses = { TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(3) };

    /// <summary>
    /// The site's sign-in configuration, read once and kept. A phone's first connection on mobile data can fail or stall
    /// while a browser still gets through, so a failed read is tried again (three attempts, each at most ten seconds); a
    /// configuration that is not the site's own is refused at once. The read starts when the app starts
    /// (<see cref="RestoreAsync"/>), so it is usually there before anyone taps Sign in; a tap meanwhile waits for the
    /// same read instead of starting another.
    /// </summary>
    private Task<Discovery> DiscoverAsync(CancellationToken ct)
    {
        if (_discovery != null) return Task.FromResult(_discovery);
        lock (_discoveryLock)
        {
            if (_discovering == null || _discovering.IsFaulted || _discovering.IsCanceled)
            {
                _discovering = ReadDiscoveryAsync();
            }
            return _discovering.WaitAsync(ct);
        }
    }

    private readonly object _discoveryLock = new();
    private Task<Discovery> _discovering;

    private async Task<Discovery> ReadDiscoveryAsync()
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var timeout = new CancellationTokenSource(AttemptTimeout);
                var discovery = await http.GetFromJsonAsync<Discovery>(AppConfig.SiteUrl + "/.well-known/openid-configuration", timeout.Token);
                // the document must be the site's own: the folder is the issuer, never the host
                if (discovery?.TokenEndpoint == null || discovery.AuthorizationEndpoint == null || !SameIssuer(discovery.Issuer, AppConfig.SiteUrl))
                {
                    throw new InvalidDataException("The sign-in configuration is not the Didin site's.");
                }
                return _discovery = discovery;
            }
            catch (Exception ex) when (ex is not InvalidDataException && attempt < RetryPauses.Length)
            {
                await Task.Delay(RetryPauses[attempt]);
            }
        }
    }

    /// <summary>New tokens from the refresh token; false (and signed out) when the site no longer accepts it.</summary>
    private async Task<bool> RefreshAsync(CancellationToken ct)
    {
        var discovery = await DiscoverAsync(ct);
        var tokens = await PostTokenAsync(discovery, new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = _refresh,
            ["client_id"] = ClientId,
        }, ct);
        if (tokens?.AccessToken == null)
        {
            Forget();
            return false;
        }
        await KeepAsync(tokens);
        return true;
    }

    private async Task<TokenResponse> PostTokenAsync(Discovery discovery, Dictionary<string, string> form, CancellationToken ct)
    {
        using var response = await http.PostAsync(discovery.TokenEndpoint, new FormUrlEncodedContent(form), ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<TokenResponse>(ct) : null;
    }

    private async Task RevokeAsync(Discovery discovery, string token, string hint, CancellationToken ct)
    {
        using var response = await http.PostAsync(discovery.RevocationEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = token,
            ["token_type_hint"] = hint,
            ["client_id"] = ClientId,
        }), ct);
    }

    private async Task KeepAsync(TokenResponse tokens)
    {
        _access = tokens.AccessToken;
        _accessUntil = DateTimeOffset.UtcNow.AddSeconds(tokens.ExpiresIn > 0 ? tokens.ExpiresIn : 300);
        if (!string.IsNullOrEmpty(tokens.RefreshToken) && tokens.RefreshToken != _refresh)
        {
            _refresh = tokens.RefreshToken;
            try { await store.SetAsync(_refresh); }
            catch { /* kept for this run */ }
        }
    }

    private async Task LoadNameAsync(CancellationToken ct)
    {
        var discovery = await DiscoverAsync(ct);
        if (discovery.UserinfoEndpoint == null || _access == null) return;
        using var request = new HttpRequestMessage(HttpMethod.Get, discovery.UserinfoEndpoint);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _access);
        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return;
        var info = await response.Content.ReadFromJsonAsync<UserInfo>(ct);
        UserName = string.IsNullOrWhiteSpace(info?.Name) ? info?.PreferredUsername : info.Name;
    }

    private void Forget()
    {
        _access = null;
        _refresh = null;
        UserName = null;
        try { store.Remove(); }
        catch { /* nothing kept */ }
    }

    private sealed class Discovery
    {
        [JsonPropertyName("issuer")] public string Issuer { get; set; }
        [JsonPropertyName("authorization_endpoint")] public string AuthorizationEndpoint { get; set; }
        [JsonPropertyName("token_endpoint")] public string TokenEndpoint { get; set; }
        [JsonPropertyName("userinfo_endpoint")] public string UserinfoEndpoint { get; set; }
        [JsonPropertyName("revocation_endpoint")] public string RevocationEndpoint { get; set; }
        [JsonPropertyName("device_authorization_endpoint")] public string DeviceAuthorizationEndpoint { get; set; }
    }

    private sealed class DeviceResponse
    {
        [JsonPropertyName("device_code")] public string DeviceCode { get; set; }
        [JsonPropertyName("user_code")] public string UserCode { get; set; }
        [JsonPropertyName("verification_uri")] public string VerificationUri { get; set; }
        [JsonPropertyName("verification_uri_complete")] public string VerificationUriComplete { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("interval")] public int Interval { get; set; }
    }

    private sealed class ErrorResponse
    {
        [JsonPropertyName("error")] public string Error { get; set; }
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; }
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    }

    private sealed class UserInfo
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("preferred_username")] public string PreferredUsername { get; set; }
    }
}
