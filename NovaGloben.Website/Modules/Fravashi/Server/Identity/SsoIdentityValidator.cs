using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.Identity;

/// <summary>
/// The real <see cref="IIdentityValidator"/>: it verifies the identity proof the Fravashi client
/// presents (the user's SSO/OAuth <b>access token</b> from the customer's IdP) by calling that IdP's
/// userinfo endpoint with the token. A 200 with a resolvable user id means the token is genuine, unexpired, and
/// belongs to that user; the broker then mints a token attributed to the <b>IdP-derived canonical id</b>, never
/// to the client-claimed user id (a client cannot mint for someone else by lying in the request body).
/// <para>
/// Which IdP(s) are trusted is deploy-time config (<see cref="BrokerIdentityOptions"/>); the validator tries each
/// in order and returns the first identity a provider vouches for. It never logs the proof. This is a network
/// call by design: the token's authenticity is the IdP's to assert, not something the broker can check offline.
/// </para>
/// </summary>
public sealed class SsoIdentityValidator(
    HttpClient httpClient,
    IOptions<BrokerServiceOptions> options,
    ILogger<SsoIdentityValidator> logger,
    UserInfoDiscoveryCache? discovered = null) : IIdentityValidator
{
    private const string DefaultGitHubUserInfoUrl = "https://api.github.com/user";
    private const string DefaultEntraUserInfoUrl = "https://graph.microsoft.com/v1.0/me";

    private readonly BrokerIdentityOptions _identity = options.Value.Identity;

    /// <summary>
    /// Discovered userinfo endpoints per issuer; an IdP's metadata does not change under us mid-process. The
    /// cache is a SINGLETON handed in, never a field of this class: this class is a typed HTTP client, which
    /// the container builds afresh for every request, so a field here was empty on every mint and discovery
    /// ran every time without anybody noticing (found when a cold mint was measured and the question was what,
    /// exactly, was warm afterwards). The default exists for tests only.
    /// </summary>
    private readonly UserInfoDiscoveryCache _discoveredUserInfo = discovered ?? new UserInfoDiscoveryCache();

    /// <summary>
    /// Discovers every oidc issuer's userinfo endpoint ahead of the first mint. That is one request per issuer,
    /// and it also opens the connection to the issuer's host, which is the same host the proof is then checked
    /// against. Returns how many issuers answered. It never throws for an issuer that does not answer: a
    /// broker that cannot reach its IdP at start must still start, and say so on the mint that needs it.
    /// </summary>
    public async Task<int> WarmAsync(CancellationToken cancellationToken)
    {
        var answered = 0;
        foreach (var provider in _identity.Providers)
        {
            if (string.Equals(provider.Kind, TrustedIdentityProvider.Oidc, StringComparison.OrdinalIgnoreCase)
                && string.IsNullOrWhiteSpace(provider.UserInfoUrl)
                && await DiscoverUserInfoAsync(provider.Issuer, cancellationToken).ConfigureAwait(false) is not null)
            {
                answered++;
            }
        }

        return answered;
    }

    public async ValueTask<ValidatedIdentity?> ValidateAsync(string userId, string? identityProof, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(identityProof))
        {
            return null;
        }

        foreach (var provider in _identity.Providers)
        {
            var identity = await VerifyAgainstAsync(provider, identityProof, cancellationToken).ConfigureAwait(false);
            if (identity is not null)
            {
                return identity;
            }
        }

        return null;
    }

    private async Task<ValidatedIdentity?> VerifyAgainstAsync(
        TrustedIdentityProvider provider, string proof, CancellationToken cancellationToken)
    {
        var kind = provider.Kind?.Trim().ToLowerInvariant();
        string? url = kind switch
        {
            TrustedIdentityProvider.GitHub => provider.UserInfoUrl ?? DefaultGitHubUserInfoUrl,
            TrustedIdentityProvider.Entra => provider.UserInfoUrl ?? DefaultEntraUserInfoUrl,
            TrustedIdentityProvider.Oidc => provider.UserInfoUrl
                ?? await DiscoverUserInfoAsync(provider.Issuer, cancellationToken).ConfigureAwait(false),
            _ => null,
        };

        if (url is null)
        {
            logger.LogWarning(
                "Ignoring a trusted identity provider with an unrecognized kind '{Kind}' or no reachable userinfo endpoint.",
                provider.Kind);
            return null;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", proof);
            request.Headers.UserAgent.ParseAdd("Fravashi-Broker");
            request.Headers.Accept.ParseAdd(
                kind == TrustedIdentityProvider.GitHub ? "application/vnd.github+json" : "application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // A rejected token is an ordinary negative outcome, not an error. Never log the proof.
                return null;
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return kind switch
            {
                TrustedIdentityProvider.GitHub => ReadGitHub(body),
                TrustedIdentityProvider.Entra => ReadEntra(body),
                _ => ReadOidc(body),
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Identity verification against {Kind} failed to reach the provider.", provider.Kind);
            return null;
        }
    }

    /// <summary>The issuer's userinfo endpoint from its discovery document; null when it cannot be read.</summary>
    private async Task<string?> DiscoverUserInfoAsync(string? issuer, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuer))
        {
            return null;
        }

        if (_discoveredUserInfo.Endpoints.TryGetValue(issuer, out var cached))
        {
            return cached;
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get, $"{issuer.TrimEnd('/')}/.well-known/openid-configuration");
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("userinfo_endpoint", out var endpoint)
                && endpoint.ValueKind == JsonValueKind.String
                && endpoint.GetString() is { Length: > 0 } found)
            {
                _discoveredUserInfo.Endpoints[issuer] = found;
                return found;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "OIDC discovery failed for the configured issuer.");
        }

        return null;
    }

    /// <summary>GitHub's user endpoint returns a numeric <c>id</c> (stable) and a <c>login</c>. Canonical: <c>github:{id}</c>.</summary>
    private static ValidatedIdentity? ReadGitHub(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("id", out var id) &&
                id.ValueKind == JsonValueKind.Number)
            {
                return new ValidatedIdentity(
                    $"github:{id.GetInt64()}", ReadString(root, "email"), ReadString(root, "login"));
            }
        }
        catch (JsonException)
        {
            // Unreadable body: treat as no identity.
        }

        return null;
    }

    /// <summary>Entra's Graph <c>/me</c> returns the object id in <c>id</c> (the token's <c>oid</c>). Canonical: <c>entra:{oid}</c>.</summary>
    private static ValidatedIdentity? ReadEntra(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && ReadString(root, "id") is { Length: > 0 } id)
            {
                return new ValidatedIdentity(
                    $"entra:{id}",
                    ReadString(root, "mail") ?? ReadString(root, "userPrincipalName"),
                    ReadString(root, "displayName"));
            }
        }
        catch (JsonException)
        {
            // Unreadable body: treat as no identity.
        }

        return null;
    }

    /// <summary>
    /// A standards-compliant userinfo response: <c>sub</c> is the stable subject. Canonical: <c>oidc:{sub}</c>,
    /// with the email/username carried for the operator-facing allowlist.
    /// </summary>
    private static ValidatedIdentity? ReadOidc(byte[] body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind == JsonValueKind.Object && ReadString(root, "sub") is { Length: > 0 } sub)
            {
                return new ValidatedIdentity(
                    $"oidc:{sub}",
                    ReadString(root, "email"),
                    ReadString(root, "preferred_username") ?? ReadString(root, "name"))
                {
                    Roles = ReadRoles(root),
                };
            }
        }
        catch (JsonException)
        {
            // Unreadable body: treat as no identity.
        }

        return null;
    }

    /// <summary>
    /// The roles a userinfo answer carries. IdPs disagree on the shape: the claim is <c>role</c> or
    /// <c>roles</c>, and either is one string or an array of strings. Anything else (an object, a number, a
    /// nested array) is not a role and is dropped rather than rendered into one.
    /// </summary>
    private static IReadOnlyList<string> ReadRoles(JsonElement root)
    {
        var roles = new List<string>();
        foreach (var claim in (string[])["role", "roles"])
        {
            if (!root.TryGetProperty(claim, out var value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                Add(value.GetString());
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in value.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.String)
                    {
                        Add(entry.GetString());
                    }
                }
            }
        }

        return roles;

        void Add(string? role)
        {
            if (!string.IsNullOrWhiteSpace(role))
            {
                roles.Add(role.Trim());
            }
        }
    }

    private static string? ReadString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
