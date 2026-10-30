using Lek.Server.Elementals;
using Lek.Server.Fuse;
using Lek.Server.Noir;
using Lek.Server.RelicRush;
using Lek.Server.Spark;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stomme.Infrastructure;

namespace Lek.Server.Startup
{
    /// <summary>Lek's services, found by the host's module scan: each game's world and its tick loop, and the site setup.</summary>
    public sealed class LekServerStartup : IServerStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // whether this host switched the module off: its reconciler, guards and middleware stand down then
            services.AddSingleton<Lek.Server.Services.LekSwitch>();
            // a person deleted from the site leaves Inside Job's list of players and is no longer named in others' raid logs
            services.AddScoped<Stomme.Erasure.ISubjectErasureParticipant, Lek.Server.Services.LekUserErasure>();
            services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>, Lek.Server.Services.LekControllersOff>();

            services.TryAddSingleton(TimeProvider.System);
            services.AddSingleton<FuseWorld>();
            services.AddSingleton<FuseStats>();
            services.AddHostedService<FuseLoop>();
            services.AddSingleton<SparkWorld>();
            services.AddHostedService<SparkLoop>();
            services.AddSingleton<ElementalsWorld>();
            services.AddSingleton<ElementalsStats>();
            services.AddHostedService<ElementalsLoop>();
            services.AddSingleton<RelicWorld>();
            services.AddHostedService<RelicLoop>();
            services.AddSingleton<NoirWorld>();
            services.AddHostedService<NoirLoop>();

            // the site-wide cap on the mail the Lek site makes the host send (the relay budget is shared)
            services.AddSingleton<Lek.Server.Services.LekMailBudget>();

            // The Lek site: created beside the host's root site and wired on every boot; no other site is touched.
            services.AddScoped<ISiteContentReconciler, LekSiteReconciler>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
        }

        public void ConfigureMvc(IMvcBuilder mvcBuilder)
        {
        }
    }
}
