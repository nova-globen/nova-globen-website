using System;
using System.Linq;
using Stomme.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NovaGloben.Website.Articles;
using NovaGloben.Website.Contact;
using NovaGloben.Website.Sites;

namespace NovaGloben.Website.Startup
{
    /// <summary>
    /// The website's server composition, found by Stomme's convention scan: the Articles module's storage-blind store,
    /// the site reconciliation that builds the sites from the repository on every boot, and two pieces of the request
    /// pipeline - the old site's addresses (one 301 each) and the Atom feed.
    /// </summary>
    public class ServerStartup : IServerStartup
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.TryAddSingleton(TimeProvider.System);

            // The Articles module: one aggregate, on whichever store the installation chose.
            services.AddModuleAggregatePersistence<Article, Guid>();
            services.AddScoped<IArticleRepository, ArticleRepository>();
            services.AddScoped<ArticleImport>();

            // The sites from the repository, on every boot.
            services.AddScoped<ISiteContentReconciler, SiteReconciler>();

            // Last registration wins over the client's HTTP service, which the host also composes on the server.
            services.AddTransient<IArticleService, ServerArticleService>();

            // The contact form: Turnstile, the rate limit, the mail.
            services.AddHttpClient(nameof(Turnstile));
            services.AddSingleton<ContactRateLimit>();
            services.AddScoped<Turnstile>();
            services.AddScoped<ContactMailer>();
        }

        public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
        {
            // Everything here is the website's own: its sites (the root and its language folders), never a module's
            // tenant or route on the same host (WebsiteScope).
            var manifests = SiteManifest.LoadAll();
            var websiteFolders = manifests.Select(m => m.Folder).ToArray();
            var folders = manifests.Where(m => m.Folder.Length > 0).Select(m => m.Folder).ToArray();
            app.UseForWebsite(websiteFolders, website =>
            {
                website.Use((context, next) => SecurityHeaders.InvokeAsync(context, () => next(context)));
                website.Use((context, next) => VisitorCookies.InvokeAsync(context, () => next(context)));
                website.Use((context, next) => ContactEndpoint.InvokeAsync(context, () => next(context)));
                ArticleAssets.Serve(website);
                website.Use((context, next) => ProductLinks.InvokeAsync(context, () => next(context)));
                website.Use((context, next) => ProductManifests.InvokeAsync(context, () => next(context)));
                website.Use((context, next) => LegacyUrls.InvokeAsync(context, () => next(context), folders));
                website.Use((context, next) => ArticleFeed.InvokeAsync(context, () => next(context)));
                website.Use((context, next) => SiteMap.InvokeAsync(context, () => next(context)));
            });
        }

        public void ConfigureMvc(IMvcBuilder mvcBuilder)
        {
        }
    }
}
