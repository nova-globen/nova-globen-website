using Didin.Server.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stomme.Infrastructure;

namespace Didin.Server.Startup
{
    /// <summary>Didin's services, found by the host's module scan.</summary>
    public sealed class DidinServerStartup : IServerStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // whether this host switched the module off: its reconciler, guards and middleware stand down then
            services.AddSingleton<Didin.Server.Services.DidinSwitch>();
            services.AddSingleton<Microsoft.Extensions.Options.IConfigureOptions<Microsoft.AspNetCore.Mvc.MvcOptions>, Didin.Server.Services.DidinControllersOff>();

            services.TryAddSingleton(System.TimeProvider.System);

            // Every document of every kind, on whichever store the installation chose; tenant-owned, site-scoped.
            services.AddModuleAggregatePersistence<DidinDocument, System.Guid>();
            services.AddScoped<DidinStore>();

            // The catalogue: read whole per site and version (the cache), projected per request (the query), brought in
            // by the site's administrators (the import), with its binaries in a folder of the data volume.
            services.AddSingleton<Didin.Server.Services.CatalogCache>();
            services.AddScoped<Didin.Server.Services.CatalogQuery>();
            services.AddSingleton<Didin.Server.Services.DidinFiles>();
            services.AddScoped<Didin.Server.Services.CatalogImport>();

            // The media: the host's own folder or WebDAV store (no default for either), read only; cast devices get a
            // sealed ticket instead of the user's sign-in.
            services.AddSingleton<Didin.Server.Services.MediaSource>();
            services.AddScoped<Didin.Server.Services.MediaFiles>();
            services.AddSingleton<Didin.Server.Services.CastTickets>();

            // Signing a browser in from a phone: pending requests, in memory, per site.
            services.AddSingleton<Didin.Server.Services.DeviceLinks>();

            // The Didin site: created beside the host's root site and wired on every boot; no other site is touched.
            services.AddScoped<Stomme.Infrastructure.ISiteContentReconciler, DidinSiteReconciler>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
        }

        public void ConfigureMvc(IMvcBuilder mvcBuilder)
        {
        }
    }
}
