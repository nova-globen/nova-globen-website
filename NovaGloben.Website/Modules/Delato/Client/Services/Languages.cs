namespace Delato.Client.Services;

/// <summary>A language the page offers: its code and its own name, written in that language.</summary>
public sealed record LanguageChoice(string Code, string Name);

/// <summary>
/// The page's two languages and the cookie that chooses one: the standard culture cookie (<c>.AspNetCore.Culture</c>),
/// in the format ASP.NET Core's <c>CookieRequestCultureProvider.MakeCookieValue</c> writes and the host's culture
/// provider reads (<c>c=fa|uic=fa</c>; the tests hold it to the real provider), set as Stomme's own language selector
/// sets it (path "/", Lax), for a year.
/// </summary>
public static class Languages
{
    public static readonly IReadOnlyList<LanguageChoice> Choices = [new("en", "English"), new("fa", "فارسی")];

    public const string CookieName = ".AspNetCore.Culture";

    public const int CookieDays = 365;

    /// <summary>The cookie's value for a language: <c>c=fa|uic=fa</c>.</summary>
    public static string CookieValue(string code) => $"c={code}|uic={code}";

    /// <summary>The offered language the page shows now: the UI culture's two-letter name, or English.</summary>
    public static string Current(System.Globalization.CultureInfo uiCulture) =>
        Choices.Any(c => c.Code == uiCulture.TwoLetterISOLanguageName) ? uiCulture.TwoLetterISOLanguageName : "en";
}
