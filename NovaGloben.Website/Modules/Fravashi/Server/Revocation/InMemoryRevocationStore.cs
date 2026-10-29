using System.Collections.Concurrent;
using Fravashi.Broker.Tokens;

namespace Fravashi.Broker.Revocation;

/// <summary>
/// In-memory <see cref="IRevocationStore"/> (the default). Thread-safe; cleared on restart (the honest
/// caveat documented on the interface). A revoked-user cutoff keeps the latest (largest) as-of time so repeated
/// revokes never narrow the window.
/// </summary>
public sealed class InMemoryRevocationStore : IRevocationStore
{
    private readonly ConcurrentDictionary<string, byte> _revokedTokenIds = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, DateTimeOffset> _userCutoffs = new(StringComparer.Ordinal);

    public void RevokeToken(string tokenId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenId);
        _revokedTokenIds[tokenId] = 1;
    }

    public void RevokeUser(string userId, DateTimeOffset asOf)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        _userCutoffs.AddOrUpdate(userId, asOf, (_, existing) => asOf > existing ? asOf : existing);
    }

    public bool IsRevoked(BrokerToken token)
    {
        ArgumentNullException.ThrowIfNull(token);
        if (_revokedTokenIds.ContainsKey(token.TokenId))
        {
            return true;
        }

        return _userCutoffs.TryGetValue(token.Subject, out var cutoff) && token.IssuedAt <= cutoff;
    }
}
