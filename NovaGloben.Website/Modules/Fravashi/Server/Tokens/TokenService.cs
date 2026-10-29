using System.Security.Cryptography;
using System.Text.Json;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Revocation;

namespace Fravashi.Broker.Tokens;

/// <summary>
/// Mints and validates the broker's per-user tokens. Symmetric HMAC-SHA256: the same in-memory
/// signing key both signs at mint and verifies at validate - no asymmetric key distribution in v1. A token is
/// <c>base64url(payloadJson).base64url(HMAC(payloadJson))</c>; the payload carries the claim set
/// (<see cref="BrokerToken"/>). Validation verifies the signature in constant time, then checks expiry, scope,
/// and revocation - each failure a distinct <see cref="TokenValidationStatus"/>. The signing key is never part
/// of a token and is never logged.
/// </summary>
public sealed class TokenService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly BrokerSecrets _secrets;
    private readonly IRevocationStore _revocations;
    private readonly TimeProvider _clock;

    public TokenService(BrokerSecrets secrets, IRevocationStore revocations, TimeProvider? clock = null)
    {
        _secrets = secrets;
        _revocations = revocations;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Mints a short-lived, per-user, scoped token and returns the compact wire string plus its claims.</summary>
    public (string Token, BrokerToken Claims) Mint(string userId, string scope, TimeSpan ttl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var now = _clock.GetUtcNow();
        var claims = new BrokerToken(
            Subject: userId,
            Scope: scope,
            TokenId: Guid.NewGuid().ToString("N"),
            IssuedAt: now,
            ExpiresAt: now.Add(ttl));

        var payload = new TokenPayload(
            claims.Subject, claims.Scope, claims.TokenId,
            claims.IssuedAt.ToUnixTimeSeconds(), claims.ExpiresAt.ToUnixTimeSeconds());

        var payloadBytes = JsonSerializer.SerializeToUtf8Bytes(payload, Json);
        var payloadSegment = Base64UrlEncode(payloadBytes);
        var signature = Base64UrlEncode(Sign(payloadBytes));
        return ($"{payloadSegment}.{signature}", claims);
    }

    /// <summary>
    /// Validates a presented token for the required scope: shape, HMAC signature (constant-time), expiry, scope,
    /// and revocation. Returns the decoded claims only when <see cref="TokenValidationStatus.Valid"/>.
    /// </summary>
    public TokenValidationResult Validate(string? token, string requiredScope)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Malformed);
        }

        var parts = token.Split('.');
        if (parts.Length != 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Malformed);
        }

        byte[] payloadBytes;
        byte[] presentedSignature;
        try
        {
            payloadBytes = Base64UrlDecode(parts[0]);
            presentedSignature = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Malformed);
        }

        var expectedSignature = Sign(payloadBytes);
        if (!CryptographicOperations.FixedTimeEquals(presentedSignature, expectedSignature))
        {
            return TokenValidationResult.Reject(TokenValidationStatus.BadSignature);
        }

        TokenPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<TokenPayload>(payloadBytes, Json);
        }
        catch (JsonException)
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Malformed);
        }

        if (payload is null || string.IsNullOrWhiteSpace(payload.Sub) ||
            string.IsNullOrWhiteSpace(payload.Jti) || string.IsNullOrWhiteSpace(payload.Scope))
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Malformed);
        }

        var claims = new BrokerToken(
            payload.Sub, payload.Scope, payload.Jti,
            DateTimeOffset.FromUnixTimeSeconds(payload.Iat),
            DateTimeOffset.FromUnixTimeSeconds(payload.Exp));

        if (_clock.GetUtcNow() >= claims.ExpiresAt)
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Expired);
        }

        if (!string.Equals(claims.Scope, requiredScope, StringComparison.Ordinal))
        {
            return TokenValidationResult.Reject(TokenValidationStatus.WrongScope);
        }

        if (_revocations.IsRevoked(claims))
        {
            return TokenValidationResult.Reject(TokenValidationStatus.Revoked);
        }

        return new TokenValidationResult(TokenValidationStatus.Valid, claims);
    }

    private byte[] Sign(byte[] payloadBytes) => HMACSHA256.HashData(_secrets.SigningKey, payloadBytes);

    private static string Base64UrlEncode(byte[] bytes)
    {
        var encoded = Convert.ToBase64String(bytes);
        return encoded.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var chars = value.Replace('-', '+').Replace('_', '/');
        var padding = chars.Length % 4;
        if (padding == 2)
        {
            chars += "==";
        }
        else if (padding == 3)
        {
            chars += "=";
        }
        else if (padding == 1)
        {
            throw new FormatException("Invalid base64url length.");
        }

        return Convert.FromBase64String(chars);
    }

    /// <summary>The on-the-wire JSON claim shape (short keys), independent of the domain <see cref="BrokerToken"/>.</summary>
    private sealed record TokenPayload(string Sub, string Scope, string Jti, long Iat, long Exp);
}
