using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenIddict.Abstractions;
using Stomme.Identity.OpenIddict;
using Stomme.Repository;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Delato.Server.Services
{
    /// <summary>
    /// The Delato app as a client of the Delato site's own authority (since Stomme 10.76 the folder is the issuer): a
    /// public client, authorization code with PKCE and refresh tokens, limited to the Delato site's aliases, so it is
    /// refused (unauthorized_client) at the root site and at every other folder site of the tenant. Registered by the
    /// site's reconciler rather than in the host's OpenIddict:Applications, because the aliases differ per host and the
    /// reconciler is what makes them. Nothing happens on a host whose provider is off.
    /// </summary>
    public static class AppClient
    {
        public const string ClientId = "delato-app";
        public const string DisplayName = "Delato app";

        /// <summary>
        /// Where the app receives its code: its own private-use scheme on Android, a loopback address on Windows (any
        /// port matches, RFC 8252 7.3). Delato's app will use exactly these.
        /// </summary>
        public static readonly string[] RedirectUris = { "se.novaglobenab.delato:/signin", "http://127.0.0.1/signin" };

        /// <summary>What the app may ask for: who the person is, a refresh token, and the site's API.</summary>
        public static readonly string[] AppScopes = { Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess, "api" };

        /// <summary>Stomme's application property naming the sites a client is limited to (SiteScopedClientHandlers).</summary>
        internal const string SitesProperty = "stomme_sites";

        /// <summary>Creates the client, or brings it up to date; a client already as described is left alone.</summary>
        public static async Task EnsureAsync(IServiceProvider provider, int siteId, ILogger logger, CancellationToken ct = default)
        {
            if (!provider.GetRequiredService<IConfiguration>().GetValue<bool>("OpenIddict:Enabled"))
            {
                return;
            }
            var applications = provider.GetService<IOpenIddictApplicationManager>();
            if (applications == null)
            {
                return;
            }

            // On a fresh install the tenant's authority store is made on its first request; this may run before that.
            var provisioner = provider.GetService<IOpenIddictStoreProvisioner>();
            if (provisioner != null)
            {
                await provisioner.EnsureProvisionedAsync();
            }

            var sites = (await provider.GetRequiredService<IAliasRepository>().GetAliasesAsync())
                .Where(alias => alias.SiteId == siteId).Select(alias => alias.Name).ToList();
            if (sites.Count == 0)
            {
                return;
            }

            var wanted = Describe(sites);
            var existing = await applications.FindByClientIdAsync(ClientId, ct);
            if (existing == null)
            {
                await applications.CreateAsync(wanted, ct);
                logger.LogInformation("Delato: the app's client {ClientId} is registered for {Sites}.", ClientId, string.Join(", ", SitesOf(wanted)));
                return;
            }

            var current = new OpenIddictApplicationDescriptor();
            await applications.PopulateAsync(current, existing, ct);
            if (!Same(current, wanted))
            {
                await applications.UpdateAsync(existing, wanted, ct);
                logger.LogInformation("Delato: the app's client {ClientId} is updated for {Sites}.", ClientId, string.Join(", ", SitesOf(wanted)));
            }
        }

        /// <summary>The client as it must be, for the site's aliases.</summary>
        internal static OpenIddictApplicationDescriptor Describe(IEnumerable<string> siteAliases)
        {
            var descriptor = new OpenIddictApplicationDescriptor
            {
                ClientId = ClientId,
                DisplayName = DisplayName,
                ClientType = ClientTypes.Public,
                ConsentType = ConsentTypes.Implicit,
            };
            descriptor.Permissions.UnionWith(new[]
            {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
            });
            descriptor.Permissions.UnionWith(AppScopes.Select(scope => Permissions.Prefixes.Scope + scope));
            descriptor.Requirements.Add(Requirements.Features.ProofKeyForCodeExchange);
            foreach (var uri in RedirectUris)
            {
                descriptor.RedirectUris.Add(new Uri(uri));
            }

            var sites = siteAliases.Select(Normalize).Where(alias => alias.Length > 0).Distinct().OrderBy(alias => alias, StringComparer.Ordinal).ToList();
            descriptor.Properties[SitesProperty] = JsonSerializer.SerializeToElement(sites);
            return descriptor;
        }

        /// <summary>An alias as Stomme compares it: trimmed, no trailing slash, lower case.</summary>
        internal static string Normalize(string? alias) => (alias ?? "").Trim().TrimEnd('/').ToLowerInvariant();

        internal static IReadOnlyList<string> SitesOf(OpenIddictApplicationDescriptor descriptor) =>
            descriptor.Properties.TryGetValue(SitesProperty, out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => Normalize(item.GetString())).OrderBy(s => s, StringComparer.Ordinal).ToList()
                : Array.Empty<string>();

        internal static bool Same(OpenIddictApplicationDescriptor current, OpenIddictApplicationDescriptor wanted) =>
            current.ClientId == wanted.ClientId
            && current.DisplayName == wanted.DisplayName
            && current.ClientType == wanted.ClientType
            && current.ConsentType == wanted.ConsentType
            && current.Permissions.SetEquals(wanted.Permissions)
            && current.Requirements.SetEquals(wanted.Requirements)
            && current.RedirectUris.Select(uri => uri.OriginalString).ToHashSet().SetEquals(wanted.RedirectUris.Select(uri => uri.OriginalString))
            && current.PostLogoutRedirectUris.Count == 0
            && SitesOf(current).SequenceEqual(SitesOf(wanted));
    }
}
