using Delato.Server.Services;
using Delato.Server.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stomme.Infrastructure;

namespace Delato.Server.Startup;

/// <summary>Delato's services, found by the host's module scan.</summary>
public sealed class DelatoServerStartup : IServerStartup
{
    public void ConfigureServices(IServiceCollection services)
    {
        // whether Delato is on for this host (off unless module.delato.enabled=true): its reconciler, guard and
        // controllers stand down when it is off
        services.AddSingleton<DelatoSwitch>();
        services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>, DelatoControllersOff>();

        services.TryAddSingleton(TimeProvider.System);

        // the site-wide cap on the mail the Delato site makes the host send (the relay budget is shared)
        services.AddSingleton<DelatoMailBudget>();

        // Every document of every kind, on whichever store the installation chose; tenant-owned, site-scoped.
        services.AddModuleAggregatePersistence<DelatoDocument, Guid>();
        services.AddScoped<DelatoStore>();
        // a person deleted from the site is unlinked from the groups they belong to; the groups' ledgers stay
        services.AddScoped<Stomme.Erasure.ISubjectErasureParticipant, Delato.Server.Services.DelatoUserErasure>();

        // The rules of the API, and who the caller is (the site's Stomme account; its e-mail only when confirmed).
        services.AddScoped<DelatoLedger>();
        services.AddScoped<IDelatoCallers, StommeDelatoCallers>();

        // The one-time import of an earlier ledger, for the site's Host or Administrators.
        services.AddScoped<DelatoImport>();

        // The Delato site: created beside the host's root site and wired on every boot; no other site is touched.
        services.AddScoped<ISiteContentReconciler, DelatoSiteReconciler>();
    }

    public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
    {
    }

    public void ConfigureMvc(IMvcBuilder mvcBuilder)
    {
    }
}
