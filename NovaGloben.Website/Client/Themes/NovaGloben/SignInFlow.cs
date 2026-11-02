using System;

namespace NovaGloben.Website.Client.Themes.NovaGloben
{
    /// <summary>
    /// Whether the login page was reached from an app signing someone in: the authorization endpoint sends a signed-out
    /// person to <c>/login</c> with the authorize request as <c>returnurl</c>. The theme then shows only the form, the
    /// logo and the legal links. The return URL is the visitor's to change, and it decides nothing but this layout.
    /// </summary>
    public static class SignInFlow
    {
        private const string AuthorizePath = "/connect/authorize";

        public static bool IsAppSignIn(string pagePath, string returnUrl, bool signedIn)
        {
            if (signedIn || pagePath != "login" || string.IsNullOrEmpty(returnUrl))
            {
                return false;
            }

            var end = returnUrl.IndexOfAny(new[] { '?', '#' });
            var path = (end < 0 ? returnUrl : returnUrl[..end]).TrimEnd('/');
            return path.EndsWith(AuthorizePath, StringComparison.OrdinalIgnoreCase);
        }
    }
}
