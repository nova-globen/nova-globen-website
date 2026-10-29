using Fravashi.Broker.Audit;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Containment;
using Fravashi.Broker.Forwarding;
using Fravashi.Broker.Identity;
using Fravashi.Broker.PromptAudit;
using Fravashi.Broker.RateLimiting;
using Fravashi.Broker.Revocation;
using Fravashi.Broker.Tokens;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Stomme.Infrastructure;

namespace Fravashi.Broker;

/// <summary>
/// The broker's services, found by the host's module scan. The host hands this no configuration, so everything that
/// depends on it is decided when first asked for: the secrets come from <see cref="BrokerState"/>, the identity
/// validator is the real IdP check when <c>Broker:Identity:Providers</c> names one and the dev/test stub otherwise,
/// and prompt inspection runs only with its key. Nothing that needs the secrets is asked for while the broker cannot
/// serve (its routes answer 503 first).
/// </summary>
public sealed class BrokerModule : IServerStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<BrokerServiceOptions>().BindConfiguration(BrokerServiceOptions.SectionName);
        services.AddSingleton<BrokerState>();
        services.AddSingleton(sp => sp.GetRequiredService<BrokerState>().Secrets
            ?? throw new InvalidOperationException("The broker's secrets were asked for while it cannot serve."));

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IRevocationStore, InMemoryRevocationStore>();
        services.AddSingleton<IUsageMeter, InMemoryUsageMeter>();

        // Identity: the real IdP check with at least one trusted provider, the stub without (a production broker
        // always names one). The warm-up discovers the providers after start and does nothing for the stub.
        services.AddSingleton<UserInfoDiscoveryCache>();
        services.AddSingleton<ConfigIdentityValidator>();
        services.AddHttpClient<SsoIdentityValidator>();
        services.AddTransient<IIdentityValidator>(sp =>
            sp.GetRequiredService<IOptions<BrokerServiceOptions>>().Value.Identity.Providers.Count > 0
                ? sp.GetRequiredService<SsoIdentityValidator>()
                : sp.GetRequiredService<ConfigIdentityValidator>());
        services.AddHostedService<IdentityWarmup>();

        services.AddSingleton<IAuditSink, LoggingAuditSink>();
        services.AddSingleton(sp => new TokenService(
            sp.GetRequiredService<BrokerSecrets>(),
            sp.GetRequiredService<IRevocationStore>(),
            sp.GetRequiredService<TimeProvider>()));
        services.AddHttpClient<IUpstreamForwarder, AnthropicForwarder>();

        // Containment by metering: which models the grant covers, and budgets in the provider's own token figures.
        // No part of it reads what a conversation says.
        services.AddSingleton(sp => new ModelPolicy(sp.GetRequiredService<IOptions<BrokerServiceOptions>>().Value.AllowedModels));
        services.AddSingleton<ITokenBudget, InMemoryTokenBudget>();
        services.AddSingleton<Suspensions>();
        services.AddSingleton(sp => sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BrokerServiceOptions>>().Value.TokenWeights);
        services.AddSingleton<ForwardContainment>();

        // Prompt inspection: every user prompt is judged before it is forwarded, when its key is configured.
        services.AddHttpClient<TypeSafeSystemOne>();
        services.AddSingleton(sp => new PromptAuditor(
            string.IsNullOrWhiteSpace(sp.GetRequiredService<BrokerState>().Secrets?.PromptAuditKey)
                ? null
                : sp.GetRequiredService<TypeSafeSystemOne>(),
            sp.GetRequiredService<IOptions<BrokerServiceOptions>>(),
            sp.GetRequiredService<TimeProvider>()));

        services.AddHostedService<BrokerStartupLog>();
    }

    /// <summary>
    /// Refuses every broker API call with 503 while the broker cannot serve. It runs after the host has chosen the
    /// endpoint and before the endpoint runs, which is where it must be: a minimal API binds its parameters - the
    /// token service, which needs the secrets - before any endpoint filter would see the call. With the module
    /// switched off no broker endpoint is chosen, and the call passes on to its 404.
    /// </summary>
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
        app.Use(async (context, next) =>
        {
            if (context.GetEndpoint() is RouteEndpoint { RoutePattern.RawText: { } pattern }
                && pattern.StartsWith(BrokerRoutes.Api, StringComparison.OrdinalIgnoreCase)
                && !context.RequestServices.GetRequiredService<BrokerState>().Ready)
            {
                await Results.Json(new { error = "broker_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable)
                    .ExecuteAsync(context);
                return;
            }
            await next(context);
        });
    }

    public void ConfigureMvc(IMvcBuilder mvcBuilder)
    {
    }
}
