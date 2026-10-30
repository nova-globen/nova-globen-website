using Fravashi.Broker.Configuration;
using Fravashi.Broker.Integrity;
using Microsoft.Extensions.Options;

namespace Fravashi.Broker;

/// <summary>
/// Whether the broker may serve, decided once from the deploy-time configuration. Its secrets (the organisation's
/// provider key and the token-signing secret) are read from configuration or the environment, or from the
/// files their <c>_FILE</c> variables name, and held in memory only; the configuration must pass the integrity
/// check. A standalone broker refused to start without them. Inside a host it must not take the host down, so it
/// fails closed on its own: every broker route answers 503 and the problems are logged at start (BrokerStartupLog).
/// </summary>
public sealed class BrokerState
{
    public BrokerState(IConfiguration configuration, IOptions<BrokerServiceOptions> options)
        : this(name => configuration[name] ?? Environment.GetEnvironmentVariable(name), options.Value)
    {
    }

    /// <summary>The same decision with the variables read through <paramref name="getEnv"/>, for tests.</summary>
    public BrokerState(Func<string, string?> getEnv, BrokerServiceOptions options)
    {
        Options = options;
        try
        {
            var secrets = BrokerSecrets.FromEnvironment(getEnv);
            var integrity = BrokerIntegrityCheck.Evaluate(secrets, Options);
            if (integrity.Ok)
            {
                Secrets = secrets;
            }
            Problems = integrity.Problems;
        }
        catch (BrokerConfigurationException ex)
        {
            Problems = [ex.Message];
        }
    }

    /// <summary>The broker's options, as bound from the <c>Broker</c> section.</summary>
    public BrokerServiceOptions Options { get; }

    /// <summary>The secrets, when the configuration is coherent; otherwise null and the broker does not serve.</summary>
    public BrokerSecrets? Secrets { get; }

    /// <summary>What keeps the broker from serving; empty when it serves. Names problems, never a value.</summary>
    public IReadOnlyList<string> Problems { get; }

    public bool Ready => Secrets is not null;

    /// <summary>When this broker started; its usage counters live in memory and count from here.</summary>
    public DateTimeOffset StartedAt { get; } = DateTimeOffset.UtcNow;
}
