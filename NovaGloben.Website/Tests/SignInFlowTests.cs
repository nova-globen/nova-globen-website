using NovaGloben.Website.Client.Themes.NovaGloben;
using Xunit;

namespace NovaGloben.Website.Tests;

/// <summary>When the login page shows only the form: an app's authorize request sent a signed-out person there.</summary>
public class SignInFlowTests
{
    [Theory]
    [InlineData("/connect/authorize?client_id=app&response_type=code")]
    [InlineData("/connect/authorize")]
    [InlineData("/connect/authorize/")]
    [InlineData("/CONNECT/Authorize?client_id=app")]
    [InlineData("/sv/connect/authorize?client_id=app")]
    public void An_authorize_request_as_the_return_url_is_an_app_signing_in(string returnUrl)
    {
        Assert.True(SignInFlow.IsAppSignIn("login", returnUrl, signedIn: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/profile")]
    [InlineData("/blog?next=/connect/authorize")]
    [InlineData("/connect/authorize-not")]
    public void Any_other_return_url_keeps_the_full_page(string returnUrl)
    {
        Assert.False(SignInFlow.IsAppSignIn("login", returnUrl, signedIn: false));
    }

    [Fact]
    public void Only_the_login_page_loses_the_site_around_it()
    {
        Assert.False(SignInFlow.IsAppSignIn("privacy", "/connect/authorize?client_id=app", signedIn: false));
    }

    [Fact]
    public void Someone_already_signed_in_keeps_the_full_page_to_find_their_way_on()
    {
        Assert.False(SignInFlow.IsAppSignIn("login", "/connect/authorize?client_id=app", signedIn: true));
    }
}
