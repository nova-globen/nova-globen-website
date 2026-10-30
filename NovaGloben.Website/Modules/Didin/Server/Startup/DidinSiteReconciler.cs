using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Didin.Server.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace Didin.Server.Startup
{
    /// <summary>
    /// The Didin site, on every boot (Stomme runs every <see cref="ISiteContentReconciler"/> per site after its own
    /// steps). One host serves other sites too, so this touches the Didin site and nothing else:
    /// <list type="number">
    /// <item>on the host's root site (the lowest-numbered site, reached without a folder) it creates the Didin site when
    /// none exists - known by its <see cref="SiteKeySetting"/> - with every root alias + "/didin", and wires it at once in
    /// a scope of its own, because the host listed the sites before this one existed;</item>
    /// <item>on the Didin site it wires it: Home carries the Media module and is for signed-in users only (anonymous
    /// visitors land on the sign-in page); the sign-in page shows Didin's form instead of the stock one; no
    /// self-registration (the register page and the template's sample pages are deleted); the site's name, theme and
    /// container are Didin's, and its head carries the scroll fix the FluentUI stylesheet needs; the Didin app is
    /// registered as a client of the site's own authority (<see cref="AppClient"/>).</item>
    /// </list>
    /// Every step changes only what differs, so a wired site is left alone.
    /// </summary>
    public sealed class DidinSiteReconciler(IServiceProvider services, IServiceScopeFactory scopes, ILogger<DidinSiteReconciler> logger) : ISiteContentReconciler
    {
        public const string SiteKeySetting = "Didin:SiteKey";
        public const string SiteKey = "didin";
        public const string SiteName = "Didin";

        internal static readonly string MediaModule = typeof(Didin.Module.Media.Index).ToModuleDefinitionName();
        internal static readonly string LoginModule = typeof(Didin.Module.Login.Index).ToModuleDefinitionName();
        private static readonly string StockLoginModule = typeof(Stomme.Modules.Admin.Login.Index).ToModuleDefinitionName();
        internal static readonly string ThemeType = TypeName(typeof(Didin.Theme.Fluent.Default));
        internal static readonly string ContainerType = TypeName(typeof(Didin.Theme.Fluent.Container));
        private static readonly string[] RemovedPaths = { "register", "private", "mypage", "privacy", "terms" };
        private const string DefaultSiteTemplate = "Stomme.Infrastructure.SiteTemplates.DefaultSiteTemplate, Stomme.Server";

        /// <summary>
        /// FluentUI's stylesheet sets <c>body{overflow:hidden}</c>, which reaches the viewport and stops the page
        /// scrolling; Stomme's alerts sit under the app's own chrome without the second rule.
        /// </summary>
        internal const string HeadContent = "<style>body{overflow:visible !important}.alert{position:relative;z-index:1000;margin-top:.5rem}</style>";

        public string Name => "Didin site";

        public async Task ReconcileAsync(SiteContentReconciliationContext context, CancellationToken ct = default)
        {
            // the host's scope for this site: its tenant and site are set
            var settings = services.GetRequiredService<ISettingRepository>();
            var site = context.Site;
            var off = services.GetService<DidinSwitch>()?.IsOff == true;

            var ours = await settings.GetSettingValueAsync(EntityNames.Site, site.SiteId, SiteKeySetting, "") == SiteKey;
            List<Site> live = null;
            var isRoot = false;
            if (!ours && string.IsNullOrEmpty(context.Alias.Path?.Trim('/')))
            {
                live = (await services.GetRequiredService<ISiteRepository>().GetSitesAsync()).Where(s => !s.IsDeleted).ToList();
                isRoot = live.Min(s => s.SiteId) == site.SiteId;
            }

            switch (Decide(off, ours, isRoot))
            {
                case Step.Wire:
                    await WireAsync(services, site);
                    break;
                case Step.Ensure:
                    await EnsureSiteAsync(context, live);
                    break;
                case Step.Hide:
                    await HideAsync(live);
                    break;
            }
        }

        public enum Step { Nothing, Wire, Ensure, Hide }

        /// <summary>
        /// What a pass does. On the Didin site: wire it, unless the module is switched off. On the host's root site:
        /// create the Didin site or give it back its aliases, or, switched off, take its aliases away. Elsewhere: nothing.
        /// Switched off, nothing is ever created or wired.
        /// </summary>
        internal static Step Decide(bool switchedOff, bool isOurSite, bool isRootSite) =>
            isOurSite ? (switchedOff ? Step.Nothing : Step.Wire)
            : isRootSite ? (switchedOff ? Step.Hide : Step.Ensure)
            : Step.Nothing;

        /// <summary>
        /// A switched-off module's site answers nowhere: its aliases are removed, so a request to its folder resolves to
        /// the root site, which has no such page (404). The site, its pages, users and data stay; switching the module
        /// back on gives the site its aliases again on the next boot (<see cref="EnsureSiteAsync"/>).
        /// </summary>
        private async Task HideAsync(List<Site> live)
        {
            var settings = services.GetRequiredService<ISettingRepository>();
            var aliases = services.GetRequiredService<IAliasRepository>();
            foreach (var candidate in live)
            {
                if (await settings.GetSettingValueAsync(EntityNames.Site, candidate.SiteId, SiteKeySetting, "") != SiteKey)
                {
                    continue;
                }
                foreach (var alias in AliasesOf(candidate.SiteId, await aliases.GetAliasesAsync()))
                {
                    await aliases.DeleteAliasAsync(alias.AliasId);
                    logger.LogInformation("Didin: switched off on this host, so alias {Alias} of site {SiteId} is removed; the site and its data stay.",
                        alias.Name, candidate.SiteId);
                }
            }
        }

        internal static List<Alias> AliasesOf(int siteId, IEnumerable<Alias> aliases) =>
            aliases.Where(a => a.SiteId == siteId).ToList();

        /// <summary>Creates the Didin site when it is missing, gives it every root alias + its folder, and wires it.</summary>
        private async Task EnsureSiteAsync(SiteContentReconciliationContext context, List<Site> live)
        {
            var settings = services.GetRequiredService<ISettingRepository>();
            var aliases = services.GetRequiredService<IAliasRepository>();

            Site didin = null;
            foreach (var candidate in live)
            {
                if (await settings.GetSettingValueAsync(EntityNames.Site, candidate.SiteId, SiteKeySetting, "") == SiteKey)
                {
                    didin = candidate;
                    break;
                }
            }

            var allAliases = (await aliases.GetAliasesAsync()).Where(a => a.TenantId == context.Alias.TenantId).ToList();
            if (didin == null)
            {
                didin = await services.GetRequiredService<ISiteRepository>().AddSiteAsync(NewSite(context.Site));
                await settings.AddSettingAsync(new Setting
                {
                    EntityName = EntityNames.Site, EntityId = didin.SiteId, SettingName = SiteKeySetting, SettingValue = SiteKey, IsPrivate = true,
                });
                logger.LogInformation("Created the Didin site {SiteId}.", didin.SiteId);
            }

            foreach (var root in allAliases.Where(a => a.SiteId == context.Site.SiteId))
            {
                var name = root.Name + "/" + DidinSite.Folder;
                var existing = allAliases.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    await aliases.AddAliasAsync(new Alias { Name = name, TenantId = context.Alias.TenantId, SiteId = didin.SiteId, IsDefault = root.IsDefault });
                    logger.LogInformation("Added alias {Alias} to the Didin site {SiteId}.", name, didin.SiteId);
                }
                else if (existing.SiteId != didin.SiteId)
                {
                    logger.LogWarning("Alias {Alias} belongs to site {Other}, not to the Didin site; left as it is.", name, existing.SiteId);
                }
            }

            var siteAliases = (await aliases.GetAliasesAsync()).Where(a => a.SiteId == didin.SiteId).ToList();
            var alias = siteAliases.FirstOrDefault(a => a.IsDefault) ?? siteAliases.FirstOrDefault();
            if (alias == null)
            {
                logger.LogWarning("The Didin site has no alias yet, so it was not wired.");
                return;
            }

            // A scope of its own, routed to the Didin site, inside the tenant scope the host already entered.
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ITenantManager>().SetAliasAsync(alias);
            var fresh = await scope.ServiceProvider.GetRequiredService<ISiteRepository>().GetSiteAsync(didin.SiteId);
            await WireAsync(scope.ServiceProvider, fresh);
        }

        internal static Site NewSite(Site root) => new()
        {
            Name = SiteName,
            TenantId = root.TenantId,
            CultureCode = "en",
            SiteTemplateType = DefaultSiteTemplate,
            DefaultThemeType = ThemeType,
            DefaultContainerType = ContainerType,
            AdminContainerType = root.AdminContainerType,
            // the catalogue is an app: interactive, on the server (nothing for the browser to download first)
            RenderMode = RenderModes.Interactive,
            Runtime = Runtimes.Server,
            Prerender = true,
            Hybrid = false,
            EnhancedNavigation = false,
            AllowRegistration = false,
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
            if (!registered.Contains(MediaModule) || !registered.Contains(LoginModule))
            {
                logger.LogError("Didin's modules are not registered on site {SiteId} ({Media}, {Login}); the site is left unwired.", site.SiteId, MediaModule, LoginModule);
                return;
            }

            var allPages = (await pages.GetPagesAsync(site.SiteId)).ToList();

            // Home is for signed-in users, or for everyone on a host whose catalogue is public (the sample); either way
            // only the catalogue's browsing is open, as Didin:PublicCatalog's policy says.
            // The module and the page each get their OWN list: saving the module stamps its rows as Module rows, and the
            // same objects on the page would then be another entity's rows, which Stomme refuses (10.76) and before that
            // would have written as site-wide rows.
            var isPublic = Services.DidinBrowsing.IsPublic(provider.GetRequiredService<Microsoft.Extensions.Configuration.IConfiguration>());
            List<Permission> HomePermissions() => isPublic ? Everyone() : SignedInOnly();
            var homePermissions = HomePermissions();
            var home = allPages.FirstOrDefault(item => item.Path == "");
            if (home != null)
            {
                await EnsureOnlyModuleAsync(provider, site, home, MediaModule, "Media", HomePermissions(), hide: _ => true);
                if (!SamePermissions(home.PermissionList, homePermissions))
                {
                    home.PermissionList = homePermissions;
                    await pages.UpdatePageAsync(home);
                    logger.LogInformation(isPublic ? "Didin: Home is open to everyone (Didin:PublicCatalog)." : "Didin: Home is for signed-in users only.");
                }
            }

            var login = allPages.FirstOrDefault(item => item.Path == "login");
            if (login != null)
            {
                await EnsureOnlyModuleAsync(provider, site, login, LoginModule, "User Login", Everyone(), hide: definition => definition == StockLoginModule);
            }

            await EnsureAdministratorsAsync(provider, site);
            await WarnSiteWideRowsAsync(provider, site);

            // the Didin app signs in at this site's own authority, as a client limited to this site
            await AppClient.EnsureAsync(provider, site.SiteId, logger);

            // a catalogue the host brings with it (Didin:SeedCatalog), into an empty catalogue only
            await provider.GetRequiredService<Services.CatalogSeed>().SeedIfEmptyAsync(new Services.DidinSite(site.SiteId, "/" + Services.DidinSite.Folder));

            foreach (var page in allPages.Where(item => RemovedPaths.Contains(item.Path) && !item.IsDeleted))
            {
                page.IsDeleted = true;
                page.DeletedBy = SiteName;
                page.DeletedOn = DateTime.UtcNow;
                await pages.UpdatePageAsync(page);
                logger.LogInformation("Didin: page /{Path} deleted.", page.Path);
            }

            if (site.Name != SiteName || site.DefaultThemeType != ThemeType || site.DefaultContainerType != ContainerType
                || site.AllowRegistration || site.HeadContent != HeadContent)
            {
                site.Name = SiteName;
                site.DefaultThemeType = ThemeType;
                site.DefaultContainerType = ContainerType;
                site.AllowRegistration = false;
                site.HeadContent = HeadContent;
                await provider.GetRequiredService<ISiteRepository>().UpdateSiteAsync(site);
                logger.LogInformation("Didin: site {SiteId} named, themed and closed to registration.", site.SiteId);
            }
        }

        /// <summary>
        /// The site's first administrators: every Host-role account of the host's root site becomes a member of
        /// the Didin site's Administrators and Registered Users roles - the same identity and password. Each grant is made
        /// once and recorded in the site's settings, so a role an administrator later removes or changes is never put back.
        /// </summary>
        private async Task EnsureAdministratorsAsync(IServiceProvider provider, Site site)
        {
            var sites = provider.GetRequiredService<ISiteRepository>();
            var roles = provider.GetRequiredService<IRoleRepository>();
            var userRoles = provider.GetRequiredService<IUserRoleRepository>();
            var settings = provider.GetRequiredService<ISettingRepository>();

            var root = (await sites.GetSitesAsync()).Where(s => !s.IsDeleted && s.SiteId != site.SiteId).Select(s => s.SiteId).DefaultIfEmpty(0).Min();
            if (root == 0) return;
            var hosts = (await userRoles.GetUserRolesAsync(RoleNames.Host, root)).Select(r => r.UserId).Distinct().ToList();
            var siteRoles = (await roles.GetRolesAsync(site.SiteId)).Where(r => r.SiteId == site.SiteId)
                .ToDictionary(r => r.Name, r => r.RoleId);
            var granted = new HashSet<int>();
            foreach (var userId in hosts)
            {
                if (await settings.GetSettingValueAsync(EntityNames.Site, site.SiteId, GrantedSetting(userId), "") != "")
                {
                    granted.Add(userId);
                }
            }

            foreach (var (userId, roleId) in PlanAdministrators(hosts, siteRoles, granted))
            {
                if (await userRoles.GetUserRoleAsync(userId, roleId) == null)
                {
                    await userRoles.AddUserRoleAsync(new UserRole { UserId = userId, RoleId = roleId, EffectiveDate = null, ExpiryDate = null });
                }
            }
            foreach (var userId in hosts.Where(id => !granted.Contains(id)))
            {
                await settings.AddSettingAsync(new Setting
                {
                    EntityName = EntityNames.Site, EntityId = site.SiteId, SettingName = GrantedSetting(userId),
                    SettingValue = DateTime.UtcNow.ToString("O"), IsPrivate = true,
                });
                logger.LogInformation("Didin: the root site's host account {UserId} is an administrator of the Didin site.", userId);
            }
        }

        internal static string GrantedSetting(int userId) => $"Didin:AdministratorGranted:{userId}";

        /// <summary>
        /// Logs, as a warning, every site-wide Module or Page permission row (EntityId -1) on the Didin site. An earlier
        /// version of this reconciler gave Home and its module one list, which Stomme before 10.76 could have written as
        /// such rows; none should exist, and this says so on every boot until any found are removed by hand.
        /// </summary>
        private async Task WarnSiteWideRowsAsync(IServiceProvider provider, Site site)
        {
            var found = await SiteWideRows.FindAsync(provider.GetRequiredService<IPermissionRepository>(), site.SiteId);
            if (found.Count == 0)
            {
                logger.LogInformation("Didin: no site-wide Module or Page permission rows on site {SiteId}.", site.SiteId);
                return;
            }
            foreach (var row in found)
            {
                logger.LogWarning("Didin: site-wide permission row {PermissionId} on site {SiteId}: {Entity} {Permission} for role {RoleId} user {UserId}, authorized {Authorized}. Remove it by hand.",
                    row.PermissionId, site.SiteId, row.EntityName, row.PermissionName, row.RoleId, row.UserId, row.IsAuthorized);
            }
        }

        /// <summary>
        /// The memberships to add: Administrators and Registered Users for every host account not granted before. Nothing
        /// when the site lacks either role (the template has not run).
        /// </summary>
        internal static List<(int UserId, int RoleId)> PlanAdministrators(IEnumerable<int> hostUserIds, IReadOnlyDictionary<string, int> siteRoles,
            ISet<int> alreadyGranted)
        {
            if (!siteRoles.TryGetValue(RoleNames.Admin, out var admin) || !siteRoles.TryGetValue(RoleNames.Registered, out var registered))
            {
                return new();
            }
            return hostUserIds.Distinct().Where(id => !alreadyGranted.Contains(id))
                .SelectMany(id => new[] { (id, admin), (id, registered) }).ToList();
        }

        /// <summary>
        /// Places <paramref name="moduleDefinition"/> on <paramref name="page"/> (once) with <paramref name="permissions"/>,
        /// and takes every other placement on that page that <paramref name="hide"/> selects off the page.
        /// </summary>
        private async Task EnsureOnlyModuleAsync(IServiceProvider services, Site site, Page page, string moduleDefinition, string title,
            List<Permission> permissions, Func<string, bool> hide)
        {
            var modules = services.GetRequiredService<IModuleRepository>();
            var pageModules = services.GetRequiredService<IPageModuleRepository>();

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
                logger.LogInformation("Didin: {Module} placed on /{Path}.", moduleDefinition, page.Path);
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

            foreach (var other in onPage.Where(item => item.Module?.ModuleDefinitionName != moduleDefinition && !item.IsDeleted && hide(item.Module?.ModuleDefinitionName)))
            {
                other.IsDeleted = true;
                other.DeletedBy = SiteName;
                other.DeletedOn = DateTime.UtcNow;
                await pageModules.UpdatePageModuleAsync(other);
                logger.LogInformation("Didin: {Module} taken off /{Path}.", other.Module?.ModuleDefinitionName, page.Path);
            }
        }

        private static List<Permission> SignedInOnly() => new()
        {
            new Permission(PermissionNames.View, RoleNames.Registered, true),
            new Permission(PermissionNames.View, RoleNames.Admin, true),
            new Permission(PermissionNames.Edit, RoleNames.Admin, true),
        };

        private static List<Permission> Everyone() => new()
        {
            new Permission(PermissionNames.View, RoleNames.Everyone, true),
            new Permission(PermissionNames.View, RoleNames.Admin, true),
            new Permission(PermissionNames.Edit, RoleNames.Admin, true),
        };

        internal static bool SamePermissions(IEnumerable<Permission> current, IEnumerable<Permission> wanted)
        {
            static string Key(Permission item) => $"{item.PermissionName}|{item.RoleName}|{item.UserId}|{item.IsAuthorized}";
            return current != null && current.Select(Key).ToHashSet().SetEquals(wanted.Select(Key));
        }

        private static string TypeName(Type type) => $"{type.FullName}, {type.Assembly.GetName().Name}";
    }
}
