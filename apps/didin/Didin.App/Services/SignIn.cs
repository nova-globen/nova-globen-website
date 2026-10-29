namespace Didin.App.Services;

/// <summary>
/// Signing the app in to its Didin site, and the access token that sign-in gives it. The site is its own issuer
/// (https://&lt;host&gt;/didin/, authorization code with PKCE, the app a public client of that site), which
/// Stomme brings in 10.76. There is deliberately no password sign-in.
/// </summary>
public interface ISignIn
{
    /// <summary>False while this app cannot sign in at all: the button says so instead of failing later.</summary>
    bool IsAvailable { get; }

    /// <summary>Why it cannot, for the person looking at the button; null when it can.</summary>
    string Unavailable { get; }

    /// <summary>The current access token, or null: the API is then called as a guest.</summary>
    string AccessToken { get; }

    /// <summary>The signed-in person's name, or null.</summary>
    string UserName { get; }

    /// <summary>Signs in; true when it did.</summary>
    Task<bool> SignInAsync(CancellationToken ct = default);

    /// <summary>Signs out: revokes at the site and forgets the tokens.</summary>
    Task SignOutAsync(CancellationToken ct = default);
}

/// <summary>
/// Until Stomme 10.76: no sign-in. It never yields a token, so the app can only browse a public catalogue (production's
/// sample) and every account's own feature stays hidden, as for a guest on the site. Failing closed is the point: no
/// stand-in sign-in that would have to be taken out again.
/// </summary>
public sealed class NoSignInYet : ISignIn
{
    public bool IsAvailable => false;
    public string Unavailable => "Signing in from the app arrives with the next version of the site's sign-in. You can browse a public catalogue meanwhile.";
    public string AccessToken => null;
    public string UserName => null;
    public Task<bool> SignInAsync(CancellationToken ct = default) => Task.FromResult(false);
    public Task SignOutAsync(CancellationToken ct = default) => Task.CompletedTask;
}
