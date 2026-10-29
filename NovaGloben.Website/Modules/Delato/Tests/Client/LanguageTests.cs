using System.Globalization;
using Delato.Client.Services;
using Microsoft.AspNetCore.Localization;

namespace Delato.Tests;

/// <summary>The in-page language switch: two languages, each named in itself, and the cookie the host reads.</summary>
public sealed class LanguageTests
{
    [Theory]
    [InlineData("fa", "c=fa|uic=fa")]
    [InlineData("en", "c=en|uic=en")]
    public void The_cookie_is_what_the_hosts_culture_provider_reads(string code, string value)
    {
        Languages.CookieValue(code).Should().Be(value);
        Languages.CookieValue(code).Should().Be(CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(code, code)));
        Languages.CookieName.Should().Be(CookieRequestCultureProvider.DefaultCookieName);

        var parsed = CookieRequestCultureProvider.ParseCookieValue(Languages.CookieValue(code))!;
        parsed.Cultures.Single().Value.Should().Be(code);
        parsed.UICultures.Single().Value.Should().Be(code);
    }

    [Fact]
    public void Each_language_is_named_in_itself_and_the_cookie_lasts_a_year()
    {
        Languages.Choices.Should().Equal(new LanguageChoice("en", "English"), new LanguageChoice("fa", "فارسی"));
        Languages.CookieDays.Should().Be(365);
    }

    [Theory]
    [InlineData("fa-IR", "fa")]
    [InlineData("fa", "fa")]
    [InlineData("en-US", "en")]
    [InlineData("sv-SE", "en")]
    public void The_current_language_is_the_ui_cultures_or_English(string culture, string current) =>
        Languages.Current(new CultureInfo(culture)).Should().Be(current);

    [Fact]
    public void The_switch_sits_in_the_modules_header_and_marks_each_link_with_its_language()
    {
        var root = Path.Combine(AppContext.BaseDirectory, "client-sources");
        var index = File.ReadAllText(Path.Combine(root, "Modules", "Ledger", "Index.razor"));
        var switcher = File.ReadAllText(Path.Combine(root, "Ui", "LanguageSwitch.razor"));

        index.Should().Contain("<div class=\"delato-top\">").And.Contain("<LanguageSwitch />");
        index.IndexOf("<LanguageSwitch />", StringComparison.Ordinal).Should().BeLessThan(index.IndexOf("<GroupList", StringComparison.Ordinal));
        switcher.Should().Contain("lang=\"@language.Code\"").And.Contain("forceLoad: true");
    }
}
