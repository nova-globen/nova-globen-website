#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NovaGloben.Website.Articles;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Makes the installation's sites match the repository, on every boot (Stomme runs every
    /// <see cref="ISiteContentReconciler"/> per site after its own install or upgrade steps). The repository is the only
    /// source of the sites' content: what the admin pages change on a managed page is put back on the next boot
    ///.
    /// </summary>
    /// <remarks>
    /// <para>On the PRIMARY site (the one the installation created, or the one already keyed "company-en"):</para>
    /// <list type="number">
    /// <item>it creates every folder site the manifests declare and the installation lacks ("sv": the Swedish site, at
    /// each primary alias + "/sv"), and reconciles each of them at once in a scope of its own, because the host's loop
    /// listed the sites before this one existed;</item>
    /// <item>it reconciles itself.</item>
    /// </list>
    /// <para>Reconciling a site: its properties (theme, static rendering, no visitor tracking, no registration, culture),
    /// its settings, robots.txt, its articles (the primary site), and its pages - every page of the manifest, and one per
    /// article, upserted through Stomme's own CreatePagesAsync with Update set; then every page this code manages that
    /// the repository no longer declares, and every page a stock site template created, is removed, and so is every
    /// section of a managed page that the manifest no longer lists. Pages of the admin site template are never touched.</para>
    /// </remarks>
    public sealed class SiteReconciler(
        IServiceScopeFactory scopes,
        ISiteRepository sites,
        IAliasRepository aliases,
        IPageRepository pages,
        IPageModuleRepository pageModules,
        ISettingRepository settings,
        ILanguageRepository languages,
        ArticleImport articleImport,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        ILogger<SiteReconciler> logger) : ISiteContentReconciler
    {
        public const string PrimaryKey = "company-en";

        public string Name => "Nova Globen sites from the repository";

        public async Task ReconcileAsync(SiteContentReconciliationContext context, CancellationToken ct = default)
        {
            var manifests = SiteManifest.LoadAll();
            var site = context.Site;
            var key = await settings.GetSettingValueAsync(EntityNames.Site, site.SiteId, WebsiteKeys.SiteKey, "");

            if (string.IsNullOrEmpty(key))
            {
                // The installation's first site becomes the primary site; any other unkeyed site is not this code's.
                var allSites = (await sites.GetSitesAsync()).Where(s => !s.IsDeleted).ToList();
                var keyed = new List<int>();
                foreach (var other in allSites)
                {
                    if (!string.IsNullOrEmpty(await settings.GetSettingValueAsync(EntityNames.Site, other.SiteId, WebsiteKeys.SiteKey, "")))
                    {
                        keyed.Add(other.SiteId);
                    }
                }
                if (keyed.Count > 0 || allSites.Min(s => s.SiteId) != site.SiteId)
                {
                    return;
                }
                key = PrimaryKey;
                await SetSettingAsync(EntityNames.Site, site.SiteId, WebsiteKeys.SiteKey, key);
                logger.LogInformation("Site {SiteId} is now the primary site ({Key}).", site.SiteId, key);
            }

            var manifest = manifests.FirstOrDefault(m => m.Key == key);
            if (manifest == null)
            {
                logger.LogWarning("Site {SiteId} is keyed {Key}, which no manifest in content/sites declares; left as it is.", site.SiteId, key);
                return;
            }

            if (manifest.Folder.Length == 0)
            {
                foreach (var folderSite in manifests.Where(m => m.Folder.Length > 0))
                {
                    await EnsureFolderSiteAsync(context, folderSite, ct);
                }
            }

            await ReconcileSiteAsync(site, context.Alias, context.TenantId, manifest, ct);
        }

        /// <summary>Creates the folder site when it is missing, gives it every primary alias + its folder, and reconciles it.</summary>
        private async Task EnsureFolderSiteAsync(SiteContentReconciliationContext context, SiteManifest manifest, CancellationToken ct)
        {
            var allAliases = (await aliases.GetAliasesAsync()).Where(a => a.TenantId == context.Alias.TenantId).ToList();
            var primaryAliases = allAliases.Where(a => a.SiteId == context.Site.SiteId).ToList();
            var wanted = primaryAliases.Select(a => (Name: a.Name + "/" + manifest.Folder, a.IsDefault)).ToList();

            Site? folderSite = null;
            foreach (var candidate in (await sites.GetSitesAsync()).Where(s => !s.IsDeleted))
            {
                if (await settings.GetSettingValueAsync(EntityNames.Site, candidate.SiteId, WebsiteKeys.SiteKey, "") == manifest.Key)
                {
                    folderSite = candidate;
                    break;
                }
            }

            if (folderSite == null)
            {
                folderSite = await sites.AddSiteAsync(NewSite(context.Site, manifest));
                await SetSettingAsync(EntityNames.Site, folderSite.SiteId, WebsiteKeys.SiteKey, manifest.Key);
                logger.LogInformation("Created site {SiteId} ({Key}).", folderSite.SiteId, manifest.Key);
            }

            foreach (var (name, isDefault) in wanted)
            {
                var existing = allAliases.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (existing == null)
                {
                    await aliases.AddAliasAsync(new Alias { Name = name, TenantId = context.Alias.TenantId, SiteId = folderSite.SiteId, IsDefault = isDefault });
                    logger.LogInformation("Added alias {Alias} to site {SiteId} ({Key}).", name, folderSite.SiteId, manifest.Key);
                }
                else if (existing.SiteId != folderSite.SiteId)
                {
                    logger.LogWarning("Alias {Alias} belongs to site {Other}, not to {Key}; left as it is.", name, existing.SiteId, manifest.Key);
                }
            }

            var folderAlias = (await aliases.GetAliasesAsync()).FirstOrDefault(a => a.SiteId == folderSite.SiteId && a.IsDefault)
                ?? (await aliases.GetAliasesAsync()).FirstOrDefault(a => a.SiteId == folderSite.SiteId);
            if (folderAlias == null)
            {
                logger.LogWarning("Site {Key} has no alias yet, so it was not reconciled.", manifest.Key);
                return;
            }

            // A scope of its own, routed to the folder site, inside the tenant scope the host already entered.
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<ITenantManager>().SetAliasAsync(folderAlias);
            var reconciler = scope.ServiceProvider.GetServices<ISiteContentReconciler>().OfType<SiteReconciler>().Single();
            var fresh = await scope.ServiceProvider.GetRequiredService<ISiteRepository>().GetSiteAsync(folderSite.SiteId);
            await reconciler.ReconcileSiteAsync(fresh, folderAlias, context.TenantId, manifest, ct);
        }

        private static Site NewSite(Site primary, SiteManifest manifest) => new Site
        {
            Name = manifest.Name,
            TenantId = primary.TenantId,
            CultureCode = manifest.Culture,
            SiteTemplateType = typeof(RepositorySiteTemplate).FullName + ", " + typeof(RepositorySiteTemplate).Assembly.GetName().Name,
            DefaultThemeType = WebsiteKeys.ThemeType,
            DefaultContainerType = WebsiteKeys.ContainerType,
            AdminContainerType = primary.AdminContainerType,
            RenderMode = RenderModes.Static,
            Runtime = Runtimes.Server,
            Prerender = false,
            Hybrid = false,
            EnhancedNavigation = false,
            AllowRegistration = false,
            CaptureBrokenUrls = false,
            VisitorTracking = false,
            PwaIsEnabled = false
        };

        internal async Task ReconcileSiteAsync(Site site, Alias alias, Stomme.Tenancy.Domain.TenantId tenantId, SiteManifest manifest, CancellationToken ct)
        {
            await ReconcileSitePropertiesAsync(site, manifest);
            await ReconcileLanguageAsync(site, manifest);

            foreach (var (name, value) in manifest.Settings)
            {
                await SetSettingAsync(EntityNames.Site, site.SiteId, name, value);
            }
            // robots.txt (served for an alias without a folder): everything may be crawled, unless this host is kept out of
            // search engines (Website:NoIndex). Stomme appends the one "Sitemap:" line itself - always, as its check for an
            // existing one never matches - and that sitemap (SiteMap) covers every site and language on the host.
            await SetSettingAsync(EntityNames.Site, site.SiteId, "Robots", SecurityHeaders.Robots(SecurityHeaders.IsNoIndex(configuration)));
            await ReconcileSmtpAsync(site);

            var templates = manifest.Pages.Select(page => ToTemplate(page, manifest)).ToList();
            if (manifest.Articles)
            {
                var imported = await articleImport.ImportAsync(tenantId, site.SiteId, ct);
                templates.AddRange(imported.Select(ArticlePage));
            }

            await sites.CreatePagesAsync(site, templates, alias);
            await RemoveWhatTheRepositoryNoLongerHasAsync(site, templates);
            logger.LogInformation("Site {SiteId} ({Key}) matches the repository: {Pages} pages.", site.SiteId, manifest.Key, templates.Count);
        }

        private async Task ReconcileSitePropertiesAsync(Site site, SiteManifest manifest)
        {
            var changed = site.Name != manifest.Name
                || site.CultureCode != manifest.Culture
                || site.DefaultThemeType != WebsiteKeys.ThemeType
                || site.DefaultContainerType != WebsiteKeys.ContainerType
                || site.AdminThemeType != Constants.DefaultTheme
                || site.RenderMode != RenderModes.Static
                || site.Runtime != Runtimes.Server
                || site.VisitorTracking
                || site.AllowRegistration
                || site.CaptureBrokenUrls
                || site.EnhancedNavigation;
            if (!changed)
            {
                return;
            }

            site.Name = manifest.Name;
            site.CultureCode = manifest.Culture;
            site.DefaultThemeType = WebsiteKeys.ThemeType;
            site.DefaultContainerType = WebsiteKeys.ContainerType;
            // The admin pages keep Stomme's own theme; the public theme styles only the public sites.
            site.AdminThemeType = Constants.DefaultTheme;
            site.RenderMode = RenderModes.Static;
            site.Runtime = Runtimes.Server;
            site.Prerender = false;
            // No visitor tracking (it sets a cookie and records visits), no self-registration, no broken-URL capture.
            site.VisitorTracking = false;
            site.AllowRegistration = false;
            site.CaptureBrokenUrls = false;
            // Every link is a full page load: the pages are static, and a page's own scripts (the contact page's
            // Turnstile) then run as on any web page, instead of depending on Blazor's enhanced navigation.
            site.EnhancedNavigation = false;
            await sites.UpdateSiteAsync(site);
        }

        /// <summary>
        /// The site's SMTP settings - Stomme's own SMTPHost, SMTPPort, SMTPSSL, SMTPSender, SMTPUsername, SMTPPassword -
        /// from configuration (<c>Website:Smtp:*</c>; the password also from the file <c>Website:Smtp:PasswordFile</c>
        /// names), so each environment's mail relay is set where the environment is, and a secret is never in the
        /// repository. Nothing configured, nothing written: the contact form then refuses to send, and says so.
        /// </summary>
        private async Task ReconcileSmtpAsync(Site site)
        {
            var host = configuration["Website:Smtp:Host"];
            if (string.IsNullOrWhiteSpace(host))
            {
                return;
            }
            var password = configuration["Website:Smtp:Password"];
            var passwordFile = configuration["Website:Smtp:PasswordFile"];
            if (string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(passwordFile) && System.IO.File.Exists(passwordFile))
            {
                password = (await System.IO.File.ReadAllTextAsync(passwordFile)).Trim();
            }

            await SetSettingAsync(EntityNames.Site, site.SiteId, "SMTPHost", host.Trim());
            await SetSettingAsync(EntityNames.Site, site.SiteId, "SMTPPort", configuration["Website:Smtp:Port"] ?? "25");
            await SetSettingAsync(EntityNames.Site, site.SiteId, "SMTPSSL", configuration["Website:Smtp:Ssl"] ?? "Auto");
            await SetSettingAsync(EntityNames.Site, site.SiteId, "SMTPSender", configuration["Website:Smtp:Sender"] ?? "");
            await SetSettingAsync(EntityNames.Site, site.SiteId, "SMTPUsername", configuration["Website:Smtp:Username"] ?? "");
            await SetSettingAsync(EntityNames.Site, site.SiteId, "SMTPPassword", password ?? "", isPrivate: true);
        }

        /// <summary>The site's one language is its culture, the default.</summary>
        private async Task ReconcileLanguageAsync(Site site, SiteManifest manifest)
        {
            var existing = (await languages.GetLanguagesAsync(site.SiteId)).ToList();
            if (existing.Any(l => l.Code == manifest.Culture && l.IsDefault))
            {
                return;
            }
            var language = existing.FirstOrDefault(l => l.Code == manifest.Culture);
            if (language == null)
            {
                await languages.AddLanguageAsync(new Language { SiteId = site.SiteId, Code = manifest.Culture, IsDefault = true, AliasName = "" });
            }
            else
            {
                language.IsDefault = true;
                await languages.UpdateLanguageAsync(language);
            }
        }

        private PageTemplate ToTemplate(PageManifest page, SiteManifest manifest)
        {
            var template = NewTemplate(page.Path, page.Name, page.Title, page.Navigation, page.Order);
            template.Url = page.Url;
            template.Settings.Add(Setting(WebsiteKeys.Description, page.Description));
            foreach (var (name, value) in page.Settings)
            {
                template.Settings.Add(Setting(name, value));
            }
            for (var i = 0; i < page.Sections.Count; i++)
            {
                template.PageTemplateModules.Add(ToModule(page.Sections[i], manifest, i + 1));
            }
            return template;
        }

        private PageTemplateModule ToModule(SectionManifest section, SiteManifest manifest, int order)
        {
            var isArticles = section.Articles.Length > 0;
            var isContact = section.Contact.Length > 0;
            var module = new PageTemplateModule
            {
                ModuleDefinitionName = isContact ? WebsiteKeys.ContactModule : isArticles ? WebsiteKeys.ArticlesModule : WebsiteKeys.PageContentModule,
                Title = section.Name,
                Pane = PaneNames.Default,
                ContainerType = WebsiteKeys.ContainerType,
                PermissionList = PublicModulePermissions()
            };
            module.Order = order;
            if (isContact)
            {
                // The public Turnstile site key from configuration; the secret never leaves the server's configuration.
                module.Settings.Add(Setting(Contact.ContactKeys.TurnstileSiteKeySetting, configuration["Contact:TurnstileSiteKey"] ?? ""));
            }
            else if (isArticles)
            {
                module.Settings.Add(Setting(WebsiteKeys.ArticlesView, section.Articles));
                module.Settings.Add(Setting(WebsiteKeys.ArticlesCount, section.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            }
            else
            {
                module.Settings.Add(Setting(WebsiteKeys.ContentKey, manifest.Key + "/" + section.Content));
            }
            return module;
        }

        /// <summary>One page per article at blog/&lt;slug&gt;, a child of the article list, not in the menu.</summary>
        private static PageTemplate ArticlePage(ArticleSummary article)
        {
            var template = NewTemplate("blog/" + article.Slug, article.Title, article.Title + " | Nova Globen AB", false, 1);
            template.Parent = "blog";
            template.Settings.Add(Setting(WebsiteKeys.Description, article.Summary));
            template.Settings.Add(Setting(WebsiteKeys.OgType, "article"));
            template.Settings.Add(Setting(WebsiteKeys.OgImage, article.CoverPng.Length > 0 ? article.CoverPng : "/assets/og-image.png"));
            template.Settings.Add(Setting("NovaGloben:Counterpart", "none"));
            template.Settings.Add(Setting(WebsiteKeys.JsonLd, ArticleJsonLd.For(article)));
            var module = new PageTemplateModule
            {
                ModuleDefinitionName = WebsiteKeys.ArticlesModule,
                Title = "article/" + article.Slug,
                Pane = PaneNames.Default,
                ContainerType = WebsiteKeys.ContainerType,
                PermissionList = PublicModulePermissions()
            };
            module.Settings.Add(Setting(WebsiteKeys.ArticlesView, "article"));
            module.Settings.Add(Setting(WebsiteKeys.ArticleSlug, article.Slug));
            template.PageTemplateModules.Add(module);
            return template;
        }

        private static PageTemplate NewTemplate(string path, string name, string title, bool navigation, int order)
        {
            var template = new PageTemplate
            {
                Name = name,
                // The home page is "/": CreatePagesAsync replaces an EMPTY path with the page's name ("Hem" became the
                // page /sv/hem, measured 2026-10-23), and maps "/" to the root.
                Path = path.Length == 0 ? "/" : path,
                Parent = path.Contains('/') ? path[..path.LastIndexOf('/')] : "",
                Title = title,
                Order = order,
                IsNavigation = navigation,
                IsClickable = true,
                IsPersonalizable = false,
                AliasName = "*",
                Version = "*",
                Update = true,
                PermissionList = new List<Permission>
                {
                    new Permission(PermissionNames.View, RoleNames.Everyone, true),
                    new Permission(PermissionNames.View, RoleNames.Admin, true),
                    new Permission(PermissionNames.Edit, RoleNames.Admin, true)
                }
            };
            template.Settings.Add(Setting(WebsiteKeys.Managed, "true"));
            return template;
        }

        private static List<Permission> PublicModulePermissions() => new()
        {
            new Permission(PermissionNames.View, RoleNames.Everyone, true),
            new Permission(PermissionNames.View, RoleNames.Admin, true),
            new Permission(PermissionNames.Edit, RoleNames.Admin, true)
        };

        private static Setting Setting(string name, string value) => new Setting { SettingName = name, SettingValue = value ?? "", IsPrivate = false };

        /// <summary>
        /// Removes what is no longer the repository's: a managed page the manifest dropped, Stomme's starter pages
        /// (Private and My Page, from its default site template), and every section of a managed page that the page no
        /// longer declares. Pages this code did not create - admin pages, pages other modules declare, a user's own
        /// pages - are never touched.
        /// </summary>
        private async Task RemoveWhatTheRepositoryNoLongerHasAsync(Site site, List<PageTemplate> templates)
        {
            var declared = new HashSet<string>(templates.Select(t => PagePath(t.Path)), StringComparer.Ordinal);
            var current = (await pages.GetPagesAsync(site.SiteId)).ToList();

            foreach (var page in current.Where(p => p.UserId == null && !declared.Contains(p.Path)))
            {
                var managed = await settings.GetSettingValueAsync(EntityNames.Page, page.PageId, WebsiteKeys.Managed, "") == "true";
                if (managed || StarterPagePaths.Contains(page.Path))
                {
                    await pages.DeletePageAsync(page.PageId);
                    logger.LogInformation("Removed page '{Path}' from site {SiteId}: the repository does not declare it.", page.Path, site.SiteId);
                }
            }

            var modules = (await pageModules.GetPageModulesAsync(site.SiteId)).ToList();
            foreach (var template in templates)
            {
                var path = PagePath(template.Path);
                var page = current.FirstOrDefault(p => p.Path == path) ?? await pages.GetPageAsync(path, site.SiteId);
                if (page == null)
                {
                    continue;
                }
                var titles = new HashSet<string>(template.PageTemplateModules.Select(m => m.Title.ToLowerInvariant()), StringComparer.Ordinal);
                foreach (var stale in modules.Where(m => m.PageId == page.PageId && !titles.Contains((m.Title ?? "").ToLowerInvariant())))
                {
                    await pageModules.DeletePageModuleAsync(stale.PageModuleId);
                    logger.LogInformation("Removed section '{Title}' from page '{Path}': the repository does not declare it.", stale.Title, page.Path);
                }
            }
        }

        /// <summary>A template path as the page stores it: lower case, the home page "/" as "".</summary>
        private static string PagePath(string templatePath) => (templatePath == "/" ? "" : templatePath ?? "").ToLowerInvariant();

        /// <summary>The pages Stomme's default site template creates besides the home page.</summary>
        private static readonly HashSet<string> StarterPagePaths = new(StringComparer.Ordinal) { "private", "mypage" };

        private async Task SetSettingAsync(string entity, int id, string name, string value, bool isPrivate = false)
        {
            var setting = await settings.GetSettingAsync(entity, id, name);
            if (setting == null)
            {
                await settings.AddSettingAsync(new Setting { EntityName = entity, EntityId = id, SettingName = name, SettingValue = value, IsPrivate = isPrivate });
            }
            else if (setting.SettingValue != value || setting.IsPrivate != isPrivate)
            {
                setting.SettingValue = value;
                setting.IsPrivate = isPrivate;
                await settings.UpdateSettingAsync(setting);
            }
        }
    }
}
