using Delato.Server.Services;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Delato.Tests;

/// <summary>The Delato app's client of the site's own authority: public, PKCE, and limited to the Delato site.</summary>
public sealed class AppClientTests
{
    private static readonly string[] Aliases = { "Example.com/delato", "www.example.com/delato/", "example.com/delato" };

    [Fact]
    public void The_client_is_public_with_pkce_and_only_the_grants_and_scopes_the_app_needs()
    {
        var client = AppClient.Describe(Aliases);

        client.ClientId.Should().Be("delato-app");
        client.ClientType.Should().Be(ClientTypes.Public);
        client.ClientSecret.Should().BeNull();
        client.Requirements.Should().BeEquivalentTo(new[] { Requirements.Features.ProofKeyForCodeExchange });
        client.Permissions.Should().BeEquivalentTo(new[]
        {
            Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation,
            Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken, Permissions.ResponseTypes.Code,
            Permissions.Prefixes.Scope + Scopes.OpenId, Permissions.Prefixes.Scope + Scopes.Profile,
            Permissions.Prefixes.Scope + Scopes.OfflineAccess, Permissions.Prefixes.Scope + "api",
        });
        client.Permissions.Should().NotContain(Permissions.GrantTypes.ClientCredentials);
        client.RedirectUris.Select(uri => uri.OriginalString).Should().BeEquivalentTo("se.novaglobenab.delato:/signin", "http://127.0.0.1/signin");
    }

    [Fact]
    public void The_client_is_limited_to_the_sites_aliases_as_Stomme_compares_them()
    {
        AppClient.SitesOf(AppClient.Describe(Aliases)).Should().Equal("example.com/delato", "www.example.com/delato");
    }

    [Fact]
    public void A_client_as_described_is_left_alone_and_a_changed_one_is_updated()
    {
        AppClient.Same(AppClient.Describe(Aliases), AppClient.Describe(Aliases.Reverse())).Should().BeTrue();

        AppClient.Same(AppClient.Describe(new[] { "example.com/delato" }), AppClient.Describe(Aliases)).Should().BeFalse("an alias was added");

        var widened = AppClient.Describe(Aliases);
        widened.Permissions.Add(Permissions.GrantTypes.ClientCredentials);
        AppClient.Same(widened, AppClient.Describe(Aliases)).Should().BeFalse("a grant was added by hand");

        var redirected = AppClient.Describe(Aliases);
        redirected.RedirectUris.Add(new Uri("https://elsewhere.example/signin"));
        AppClient.Same(redirected, AppClient.Describe(Aliases)).Should().BeFalse("a redirect was added by hand");
    }
}
