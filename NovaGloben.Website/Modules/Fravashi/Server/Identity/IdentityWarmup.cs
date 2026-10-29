using System.Collections.Concurrent;

namespace Fravashi.Broker.Identity;

/// <summary>
/// The userinfo endpoints OIDC discovery found, per issuer, for the life of the process. A singleton on
/// purpose: <see cref="SsoIdentityValidator"/> is a typed HTTP client and is built afresh for every request,
/// so anything it must remember between two mints has to live outside it.
/// </summary>
public sealed class UserInfoDiscoveryCache
{
    /// <summary>Issuer to userinfo endpoint. Issuers are compared without regard to case, as hosts are.</summary>
    public ConcurrentDictionary<string, string> Endpoints { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Runs OIDC discovery once at start, in the background, so the first mint after a deploy does not pay for it.
/// Measured on a running broker: the first mint after a container start took 12.5 seconds and the next three
/// 0.2 to 0.3, and every deploy restarts the container. It runs AFTER the host has started and
/// never blocks or fails it: a broker whose IdP is unreachable at start still serves /healthz, still refuses
/// honestly, and discovers on the first mint instead, exactly as before.
/// </summary>
public sealed class IdentityWarmup(IServiceProvider services, ILogger<IdentityWarmup> logger) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            using var scope = services.CreateScope();
            if (scope.ServiceProvider.GetRequiredService<IIdentityValidator>() is SsoIdentityValidator validator)
            {
                var answered = await validator.WarmAsync(stoppingToken).ConfigureAwait(false);
                logger.LogInformation("Identity warm-up: {Answered} OIDC issuer(s) discovered ahead of the first mint.", answered);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Identity warm-up did not finish; the first mint will discover instead.");
        }
    }
}
