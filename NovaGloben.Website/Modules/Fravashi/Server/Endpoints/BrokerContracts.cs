using System.Text.Json;

namespace Fravashi.Broker.Endpoints;

/// <summary>Mint request: a claimed user id plus an identity proof the <c>IIdentityValidator</c> checks (the stub, or the real SSO check).</summary>
public sealed record MintRequest(string? UserId, string? IdentityProof, string? Scope);

/// <summary>
/// Mint response: the minted token, its id (<c>jti</c>), its expiry, and the canonical subject the broker
/// attributed it to. The token value is returned once, to the client. <see cref="Subject"/> is the IdP-DERIVED
/// id (e.g. <c>oidc:{sub}</c>), never the client's claim, so the client can show who it is actually signed in as
/// instead of echoing its own guess.
/// </summary>
public sealed record MintResponse(string Token, string TokenId, DateTimeOffset ExpiresAt, string? Subject = null);

/// <summary>
/// Revoke request (self-service). With <c>AllMyTokens</c> the caller revokes every token they hold; otherwise
/// the caller's presented token is revoked. Revoking another user's tokens is an operator action.
/// </summary>
public sealed record RevokeRequest(bool AllMyTokens);

/// <summary>
/// What an operator sends to read what the broker has been doing or to lift a suspension. The
/// identity proof is the same one minting takes, so an operator needs no second credential and there is no
/// shared secret to leak. <paramref name="User"/> names the user being asked about, or lifted.
/// </summary>
public sealed record OperatorRequest(string? UserId, string? IdentityProof, string? User);

/// <summary>Helpers for reading non-secret fields out of a forward body for audit, without disturbing passthrough.</summary>
public static class MessagesRequest
{
    /// <summary>Best-effort parse of the <c>model</c> field from a Messages request body; null if absent/unparseable.</summary>
    public static string? TryReadModel(byte[] body)
    {
        if (body is null || body.Length == 0)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind == JsonValueKind.Object &&
                doc.RootElement.TryGetProperty("model", out var model) &&
                model.ValueKind == JsonValueKind.String)
            {
                return model.GetString();
            }
        }
        catch (JsonException)
        {
            // Not JSON we can read; audit records a null model. Never throws on the forward path.
        }

        return null;
    }
}
