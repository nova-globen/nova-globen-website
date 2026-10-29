using Lek.Server.Services;
using Microsoft.Extensions.Configuration;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace Lek.Server.Startup
{
    /// <summary>
    /// The Lek site, on every boot (Stomme runs every <see cref="ISiteContentReconciler"/> per site after its own steps).
    /// One host serves other sites too, so this touches the Lek site and nothing else:
    /// <list type="number">
    /// <item>on the host's root site (the lowest-numbered site, reached without a folder) it creates the Lek site when none
    /// exists - known by its <see cref="SiteKeySetting"/> - with every root alias + "/lek", and wires it at once in a
    /// scope of its own, because the host listed the sites before this one existed;</item>
    /// <item>on the Lek site it wires it: Home lists the games and each game has its page, open to everyone; anyone may
    /// register, and an account signs in once its e-mail is confirmed - the confirmation goes out through
    /// the mail settings in <c>Lek:Mail</c>, written to the site and sent by Stomme's notification job; the template's
    /// sample pages are deleted; the site is named Lek and its head links the games' stylesheet.</item>
    /// </list>
    /// Every step changes only what differs, so a wired site is left alone.
    /// </summary>
    public sealed class LekSiteReconciler(IServiceProvider services, IServiceScopeFactory scopes, IConfiguration configuration,
        ILogger<LekSiteReconciler> logger) : ISiteContentReconciler
    {
        public const string SiteKeySetting = "Lek:SiteKey";
        public const string SiteKey = "lek";
        public const string SiteName = "Lek";
        private static readonly string[] RemovedPaths = { "private", "mypage" };

        internal static readonly string HomeModule = typeof(Lek.Module.Home.Index).ToModuleDefinitionName();

        /// <summary>Each game's page: its path, its menu name, its module and its place in the menu.</summary>
        internal static readonly (string Path, string Name, string Module, int Order)[] Games =
        {
            ("fuse", "Fuse", typeof(Lek.Module.Fuse.Index).ToModuleDefinitionName(), 2),
            ("silent-hour", "Silent Hour", typeof(Lek.Module.SilentHour.Index).ToModuleDefinitionName(), 3),
            ("spark", "Spark", typeof(Lek.Module.Spark.Index).ToModuleDefinitionName(), 4),
            ("inside-job", "Inside Job", typeof(Lek.Module.InsideJob.Index).ToModuleDefinitionName(), 5),
            ("elementals", "Elementals", typeof(Lek.Module.Elementals.Index).ToModuleDefinitionName(), 6),
            ("elementals-classic", "Elementals (classic art)", typeof(Lek.Module.ElementalsClassic.Index).ToModuleDefinitionName(), 7),
            ("relic-rush", "Relic Rush", typeof(Lek.Module.RelicRush.Index).ToModuleDefinitionName(), 8),
            ("bullet-noir", "Bullet Noir", typeof(Lek.Module.Noir.Index).ToModuleDefinitionName(), 9),
        };

        public string Name => "Lek site";

        public async Task ReconcileAsync(SiteContentReconciliationContext context, CancellationToken ct = default)
        {
            // the host's scope for this site: its tenant and site are set
            var settings = services.GetRequiredService<ISettingRepository>();
            var site = context.Site;

            if (await settings.GetSettingValueAsync(EntityNames.Site, site.SiteId, SiteKeySetting, "") == SiteKey)
            {
                await WireAsync(services, site);
                return;
            }

            if (!string.IsNullOrEmpty(context.Alias.Path?.Trim('/')))
            {
                return;
            }
            var live = (await services.GetRequiredService<ISiteRepository>().GetSitesAsync()).Where(s => !s.IsDeleted).ToList();
            if (live.Min(s => s.SiteId) != site.SiteId)
            {
                return;
            }
            await EnsureSiteAsync(context, live);
        }

        /// <summary>Creates the Lek site when it is missing, gives it every root alias + its folder, and wires it.</summary>
        private async Task EnsureSiteAsync(SiteContentReconciliationContext context, List<Site> live)
        {
            var settings = services.GetRequiredService<ISettingRepository>();
            var aliases = services.GetRequiredService<IAliasRepository>();

            Site? lek = null;
            foreach (var candidate in live)
            {
                if (await settings.GetSettingValueAsync(EntityNames.Site, candidate.SiteId, SiteKeySetting, "") == SiteKey)
                {
                    lek = candidate;
                    break;
                }
            }

            var allAliases = (await aliases.GetAliasesAsync()).Where(a => a.TenantId == context.Alias.TenantId).ToList();
            if (lek == null)
            {
                lek = await services.GetRequiredService<ISiteRepository>().AddSiteAsync(NewSite(context.Site));
                await settings.AddSettingAsync(new Setting
                {
                    EntityName = EntityNames.Site, EntityId = lek.SiteId, SettingName = SiteKeySetting, SettingValue = SiteKey, IsPrivate = true,
                });
                logger.LogInformation("Created the Lek site {SiteId}.", lek.SiteId);
            }

            foreach (var root in allAliases.Where(a => a.SiteId == context.Site.SiteId))
            {
                var name = root.Name + "/" + LekSite.Folder;
                var existing = allAliases.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    await aliases.AddAliasAsync(new Alias { Name = name, TenantId = context.Alias.TenantId, SiteId = lek.SiteId, IsDefault = root.IsDefault });
                    logger.LogInformation("Added alias {Alias} to the Lek site {SiteId}.", name, lek.SiteId);
                }
                else if (existing.SiteId != lek.SiteId)
                {
                    logger.LogWarning("Alias {Alias} belongs to site {Other}, not to the Lek site; left as it is.", name, existing.SiteId);
                }
            }

            var siteAliases = (await aliases.GetAliasesAsync()).Where(a => a.SiteId == lek.SiteId).ToList();
            var alias = siteAliases.FirstOrDefault(a => a.IsDefault) ?? siteAliases.FirstOrDefault();
            if (alias == null)
            {
                logger.LogWarning("The Lek site has no alias yet, so it was not wired.");
                return;
            }

            // A scope of its own, routed to the Lek site, inside the tenant scope the host already entered.
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ITenantManager>().SetAliasAsync(alias);
            var fresh = await scope.ServiceProvider.GetRequiredService<ISiteRepository>().GetSiteAsync(lek.SiteId);
            await WireAsync(scope.ServiceProvider, fresh);
        }

        internal static Site NewSite(Site root) => new()
        {
            Name = SiteName,
            TenantId = root.TenantId,
            CultureCode = "en",
            SiteTemplateType = Constants.DefaultSiteTemplate,
            DefaultThemeType = Constants.DefaultTheme,
            DefaultContainerType = Constants.DefaultContainer,
            AdminContainerType = root.AdminContainerType,
            // the games are static pages in frames, talking to their hubs from the browser
            RenderMode = RenderModes.Static,
            Runtime = Runtimes.Server,
            Prerender = false,
            Hybrid = false,
            EnhancedNavigation = false,
            AllowRegistration = true,
            CaptureBrokenUrls = false,
            VisitorTracking = false,
            PwaIsEnabled = false,
        };

        private async Task WireAsync(IServiceProvider provider, Site site)
        {
            var definitions = provider.GetRequiredService<IModuleDefinitionRepository>();
            var pages = provider.GetRequiredService<IPageRepository>();

            // Reading the definitions registers the ones found in the loaded assemblies.
            var registered = (await definitions.GetModuleDefinitionsAsync(site.SiteId)).Select(item => item.ModuleDefinitionName).ToHashSet();
            var missing = Games.Select(g => g.Module).Append(HomeModule).Where(m => !registered.Contains(m)).ToList();
            if (missing.Count > 0)
            {
                logger.LogError("Lek's modules are not registered on site {SiteId} ({Missing}); the site is left unwired.", site.SiteId, string.Join(", ", missing));
                return;
            }

            var allPages = (await pages.GetPagesAsync(site.SiteId)).ToList();
            var home = allPages.FirstOrDefault(item => item.Path == "");
            if (home != null)
            {
                await EnsureOnlyModuleAsync(provider, site, home, HomeModule, "Games");
            }
            foreach (var (path, name, module, order) in Games)
            {
                await EnsureGamePageAsync(provider, site, allPages, path, name, module, order);
            }

            // the template's sample pages have no place on a game site (soft-deleted pages still count as existing, so the
            // framework's template top-up does not bring them back)
            foreach (var page in allPages.Where(item => RemovedPaths.Contains(item.Path) && !item.IsDeleted))
            {
                page.IsDeleted = true;
                page.DeletedBy = SiteName;
                page.DeletedOn = DateTime.UtcNow;
                await pages.UpdatePageAsync(page);
                logger.LogInformation("Lek: page /{Path} deleted.", page.Path);
            }

            if (site.Name != SiteName || !site.AllowRegistration || site.HeadContent != HeadContent)
            {
                site.Name = SiteName;
                site.AllowRegistration = true;
                site.HeadContent = HeadContent;
                await provider.GetRequiredService<ISiteRepository>().UpdateSiteAsync(site);
                logger.LogInformation("Lek: site {SiteId} named and open to registration.", site.SiteId);
            }

            var settings = provider.GetRequiredService<ISettingRepository>();
            foreach (var (name, value) in SiteSettings(configuration))
            {
                await EnsureSettingAsync(settings, site, name, value);
            }
            if (MailSettings(configuration).Count > 0)
            {
                await EnableNotificationJobAsync(provider);
            }
            else
            {
                logger.LogWarning("Lek: no Lek:Mail settings on this host, so a new account's confirmation e-mail cannot be sent.");
            }
        }

        /// <summary>The site's head: the stylesheet for the game list and the game frames, by its content hash.</summary>
        internal static string HeadContent => $"<link rel=\"stylesheet\" href=\"{LekAssets.Root}games/site.css?v={LekAssets.Hash("games/site.css")}\" />";

        /// <summary>The site settings Lek keeps: a new account confirms its e-mail before it can sign in, and the mail settings.</summary>
        internal static List<(string Name, string Value)> SiteSettings(IConfiguration configuration)
        {
            var all = new List<(string, string)> { ("LoginOptions:RequireConfirmedEmail", "true") };
            all.AddRange(MailSettings(configuration));
            return all;
        }

        /// <summary>
        /// Stomme's SMTP settings for the site from <c>Lek:Mail:Host</c>, <c>Port</c> (25), <c>Sender</c> and <c>Ssl</c>
        /// (None): plain SMTP to the host's relay, which takes no credential. None when Host or Sender is missing.
        /// </summary>
        internal static List<(string Name, string Value)> MailSettings(IConfiguration configuration)
        {
            var host = configuration["Lek:Mail:Host"];
            var sender = configuration["Lek:Mail:Sender"];
            if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender))
            {
                return new();
            }
            return new()
            {
                ("SMTPEnabled", "True"),
                ("SMTPHost", host.Trim()),
                ("SMTPPort", string.IsNullOrWhiteSpace(configuration["Lek:Mail:Port"]) ? "25" : configuration["Lek:Mail:Port"]!.Trim()),
                ("SMTPSSL", string.IsNullOrWhiteSpace(configuration["Lek:Mail:Ssl"]) ? "None" : configuration["Lek:Mail:Ssl"]!.Trim()),
                ("SMTPAuthentication", "Basic"),
                ("SMTPSender", sender.Trim()),
            };
        }

        private async Task EnsureSettingAsync(ISettingRepository settings, Site site, string name, string value)
        {
            var current = await settings.GetSettingAsync(EntityNames.Site, site.SiteId, name);
            if (current == null)
            {
                await settings.AddSettingAsync(new Setting { EntityName = EntityNames.Site, EntityId = site.SiteId, SettingName = name, SettingValue = value, IsPrivate = true });
            }
            else if (current.SettingValue != value)
            {
                current.SettingValue = value;
                await settings.UpdateSettingAsync(current);
            }
        }

        /// <summary>
        /// Stomme's notification job sends every site's queued mail and is off by default; a host that names a relay for
        /// Lek switches it on (as Stomme's configured backup schedule does for its job). A site without SMTP settings is
        /// skipped by the job.
        /// </summary>
        private async Task EnableNotificationJobAsync(IServiceProvider provider)
        {
            var jobs = provider.GetRequiredService<IJobRepository>();
            var jobType = Utilities.GetFullTypeName(typeof(NotificationJob).AssemblyQualifiedName);
            var job = await jobs.GetJobAsync(jobType);
            if (job is null)
            {
                var template = new NotificationJob(provider.GetRequiredService<IServiceScopeFactory>());
                await jobs.AddJobAsync(new Job
                {
                    JobType = jobType,
                    Name = template.Name,
                    Frequency = template.Frequency,
                    Interval = template.Interval,
                    StartDate = template.StartDate,
                    EndDate = template.EndDate,
                    MaximumDuration = template.MaximumDuration,
                    RetentionHistory = template.RetentionHistory,
                    IsEnabled = true,
                    IsStarted = true,
                    IsExecuting = false,
                    NextExecution = null,
                });
                logger.LogInformation("Lek: the notification job is on, for the site's confirmation e-mail.");
            }
            else if (!job.IsEnabled)
            {
                job.IsEnabled = true;
                await jobs.UpdateJobAsync(job);
                logger.LogInformation("Lek: the notification job is on, for the site's confirmation e-mail.");
            }
        }

        private async Task EnsureGamePageAsync(IServiceProvider provider, Site site, List<Page> allPages, string path, string name, string module, int order)
        {
            var pages = provider.GetRequiredService<IPageRepository>();
            var page = allPages.FirstOrDefault(item => item.Path == path);
            if (page == null)
            {
                page = await pages.AddPageAsync(new Page
                {
                    SiteId = site.SiteId,
                    Path = path,
                    Name = name,
                    Title = name,
                    Order = order,
                    Url = "",
                    ThemeType = "",
                    DefaultContainerType = "",
                    HeadContent = "",
                    BodyContent = "",
                    Icon = "",
                    IsNavigation = true,
                    IsClickable = true,
                    IsPersonalizable = false,
                    UserId = null,
                    PermissionList = Everyone(),
                });
                allPages.Add(page);
                logger.LogInformation("Lek: page /{Path} created.", path);
            }
            else if (page.IsDeleted)
            {
                page.IsDeleted = false;
                page.DeletedBy = null;
                page.DeletedOn = null;
                await pages.UpdatePageAsync(page);
            }
            await EnsureOnlyModuleAsync(provider, site, page, module, name);
        }

        /// <summary>Places the module on the page once, with everyone allowed to view, and takes every other placement off.</summary>
        private async Task EnsureOnlyModuleAsync(IServiceProvider provider, Site site, Page page, string moduleDefinition, string title)
        {
            var modules = provider.GetRequiredService<IModuleRepository>();
            var pageModules = provider.GetRequiredService<IPageModuleRepository>();
            var permissions = Everyone();

            var onPage = (await pageModules.GetPageModulesAsync(site.SiteId)).Where(item => item.PageId == page.PageId).ToList();
            var ours = onPage.FirstOrDefault(item => item.Module?.ModuleDefinitionName == moduleDefinition);
            if (ours == null)
            {
                var module = await modules.AddModuleAsync(new Stomme.Models.Module
                {
                    SiteId = site.SiteId,
                    ModuleDefinitionName = moduleDefinition,
                    AllPages = false,
                    PermissionList = permissions,
                });
                await pageModules.AddPageModuleAsync(new PageModule
                {
                    PageId = page.PageId,
                    ModuleId = module.ModuleId,
                    Title = title,
                    Pane = PaneNames.Default,
                    Order = 1,
                    ContainerType = "",
                });
                logger.LogInformation("Lek: {Module} placed on /{Path}.", moduleDefinition, page.Path);
            }
            else
            {
                if (ours.IsDeleted)
                {
                    ours.IsDeleted = false;
                    ours.DeletedBy = null;
                    ours.DeletedOn = null;
                    await pageModules.UpdatePageModuleAsync(ours);
                }
                if (!SamePermissions(ours.Module.PermissionList, permissions))
                {
                    ours.Module.PermissionList = permissions;
                    await modules.UpdateModuleAsync(ours.Module);
                }
            }

            foreach (var other in onPage.Where(item => item.Module?.ModuleDefinitionName != moduleDefinition && !item.IsDeleted))
            {
                other.IsDeleted = true;
                other.DeletedBy = SiteName;
                other.DeletedOn = DateTime.UtcNow;
                await pageModules.UpdatePageModuleAsync(other);
                logger.LogInformation("Lek: {Module} taken off /{Path}.", other.Module?.ModuleDefinitionName, page.Path);
            }
        }

        private static List<Permission> Everyone() => new()
        {
            new Permission(PermissionNames.View, RoleNames.Everyone, true),
            new Permission(PermissionNames.View, RoleNames.Admin, true),
            new Permission(PermissionNames.Edit, RoleNames.Admin, true),
        };

        internal static bool SamePermissions(IEnumerable<Permission>? current, IEnumerable<Permission> wanted)
        {
            static string Key(Permission item) => $"{item.PermissionName}|{item.RoleName}|{item.UserId}|{item.IsAuthorized}";
            return current != null && current.Select(Key).ToHashSet().SetEquals(wanted.Select(Key));
        }
    }
}
