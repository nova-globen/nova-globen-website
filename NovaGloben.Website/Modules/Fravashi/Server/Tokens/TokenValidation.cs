namespace Fravashi.Broker.Tokens;

/// <summary>Why a presented token was accepted or rejected. Every rejection is a distinct, auditable reason.</summary>
public enum TokenValidationStatus
{
    /// <summary>Signature verified, not expired, correct scope, not revoked.</summary>
    Valid,

    /// <summary>The token could not be parsed (wrong shape, bad base64, unparseable claims).</summary>
    Malformed,

    /// <summary>The HMAC signature did not verify against the signing key (tampered or forged).</summary>
    BadSignature,

    /// <summary>The token is past its expiry.</summary>
    Expired,

    /// <summary>The token's scope does not include the required scope.</summary>
    WrongScope,

    /// <summary>The token (by jti) or the user (by revoke-all cutoff) has been revoked.</summary>
    Revoked,
}

/// <summary>The outcome of validating a presented token: the status and, when <see cref="TokenValidationStatus.Valid"/>, the claims.</summary>
/// <param name="Status">Accept/reject reason.</param>
/// <param name="Token">The decoded claims when valid; otherwise null.</param>
public sealed record TokenValidationResult(TokenValidationStatus Status, BrokerToken? Token)
{
    /// <summary>True only when the token is valid.</summary>
    public bool IsValid => Status == TokenValidationStatus.Valid && Token is not null;

    /// <summary>A short, secret-free reason string for the audit log.</summary>
    public string Reason => Status.ToString().ToLowerInvariant();

    internal static TokenValidationResult Reject(TokenValidationStatus status) => new(status, null);
}
