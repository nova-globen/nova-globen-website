using Fravashi.Broker.Configuration;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker.Identity;

/// <summary>
/// The stub <see cref="IIdentityValidator"/>. It is <b>not</b> real identity verification and
/// says so: when <see cref="BrokerServiceOptions.StubIdentityProof"/> is configured it accepts a matching proof
/// for any non-empty user id; when it is unset it accepts any non-empty user id (dev default). Either way it lets
/// mint and drive the whole chain end-to-end. In production the real IdP check (SsoIdentityValidator) takes its
/// place behind the same interface - this validator does not ship enabled in the corporate tier.
/// </summary>
public sealed class ConfigIdentityValidator(IOptions<BrokerServiceOptions> options) : IIdentityValidator
{
    private readonly BrokerServiceOptions _options = options.Value;

    public ValueTask<ValidatedIdentity?> ValidateAsync(string userId, string? identityProof, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return ValueTask.FromResult<ValidatedIdentity?>(null);
        }

        var required = _options.StubIdentityProof;
        if (!string.IsNullOrWhiteSpace(required) &&
            !string.Equals(required, identityProof, StringComparison.Ordinal))
        {
            return ValueTask.FromResult<ValidatedIdentity?>(null);
        }

        return ValueTask.FromResult<ValidatedIdentity?>(new ValidatedIdentity(userId.Trim()));
    }
}
