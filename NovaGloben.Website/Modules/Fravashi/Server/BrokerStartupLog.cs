using Fravashi.Broker.Configuration;
using Fravashi.Broker.PromptAudit;

namespace Fravashi.Broker;

/// <summary>
/// What the broker says once the host starts: either why it refuses to serve, or how it is configured - the
/// provider, the limits, the model list, prompt inspection and identity verification, each at the level an operator
/// must not miss. It reveals no secret.
/// </summary>
public sealed class BrokerStartupLog(BrokerState state, IServiceProvider services, ILogger<BrokerStartupLog> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (!state.Ready)
        {
            logger.LogError(
                "The Fravashi broker REFUSES TO SERVE: {Problems}. Every route under {Api} answers 503 until the deploy-time "
                + "configuration is fixed and the host restarted.",
                string.Join(" | ", state.Problems), BrokerRoutes.Api);
            return Task.CompletedTask;
        }

        var options = state.Options;
        logger.LogInformation(
            "Fravashi broker serving at {Api}: provider={Provider}, token TTL={Ttl}, rate={Rate}/{RateWindow}, "
            + "quota={Quota}/{QuotaWindow}. Secrets are held in memory only.",
            BrokerRoutes.Api, options.ProviderBaseUrl, options.TokenTtl, options.RateLimitRequests, options.RateLimitWindow,
            options.QuotaRequests, options.QuotaWindow);

        if (options.AllowedModels.Count == 0)
        {
            logger.LogWarning(
                "No models are named in Broker:AllowedModels, so ANY model the provider offers is forwarded. "
                + "Name the models this grant covers to hold requests to them.");
        }

        logger.LogInformation(
            "Token budgets, in weighted units (input {Input}, cache write {CacheWrite}, cache read {CacheRead}, output {Output} "
            + "each): per user {UserTokens}/{QuotaWindow}, organisation {OrganisationTokens}/day (0 means no limit); "
            + "an answer whose usage cannot be read is charged {Unreadable}. Counters are in memory and reset on restart.",
            options.TokenWeights.Input, options.TokenWeights.CacheWrite, options.TokenWeights.CacheRead, options.TokenWeights.Output,
            options.QuotaTokens, options.QuotaWindow, options.OrganisationTokensPerDay, options.UnreadableUsageCharge);

        var auditor = services.GetRequiredService<PromptAuditor>();
        if (auditor.IsOn)
        {
            logger.LogInformation(
                "Prompt inspection: ON through {Provider} (model {Model}); risk from {CautionAt} is recorded as cautioned, "
                + "from {RefuseAt} is refused; with no judgement a request is {Fallback}.",
                auditor.ModelName, options.PromptAudit.Model, options.PromptAudit.CautionAt, options.PromptAudit.RefuseAt,
                options.PromptAudit.WhenUnavailable == PromptAuditFallback.Refuse ? "refused" : "forwarded");
        }
        else
        {
            logger.LogWarning(
                "Prompt inspection: OFF. No key is configured ({KeyVariable} or its _FILE), so prompts are forwarded "
                + "without being judged.", BrokerSecrets.PromptAuditKeyEnvVar);
        }

        if (options.Identity.Providers.Count > 0)
        {
            logger.LogInformation("Identity verification: real IdP ({Providers}).",
                string.Join(", ", options.Identity.Providers.Select(p => p.Kind)));
        }
        else
        {
            logger.LogWarning(
                "Identity verification: DEV/TEST stub (no Broker:Identity:Providers configured). Do NOT run this in production.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
