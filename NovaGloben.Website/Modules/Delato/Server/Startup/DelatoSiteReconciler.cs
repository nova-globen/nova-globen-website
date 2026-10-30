using Delato.Server.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace Delato.Server.Startup;

/// <summary>
/// The Delato site, on every boot (Stomme runs every <see cref="ISiteContentReconciler"/> per site after its own steps).
/// One host serves other sites too, so this touches the Delato site and nothing else - the same pattern as the Lek
/// module's site:
/// <list type="number">
/// <item>on the host's root site (the lowest-numbered site, reached without a folder) it creates the Delato site when
/// none exists - known by its <see cref="SiteKeySetting"/> - with every root alias + "/delato", and wires it at once in
/// a scope of its own, because the host listed the sites before this one existed;</item>
/// <item>on the Delato site it wires it: Home carries the ledger module, for signed-in accounts only (anonymous visitors
/// are sent to sign in), and nothing else; anyone may register, and an account signs in once its e-mail is confirmed
/// (Delato links an account to a member by its confirmed e-mail only); the confirmation goes out through the mail
/// settings in <c>Delato:Mail</c>, written to the site and sent by Stomme's notification job; the site's languages
/// are English (the default) and Persian; the template's sample pages are deleted; the site is named Delato; Delato's
/// app is registered as a client of the site's own authority (<see cref="AppClient"/>).</item>
/// </list>
/// Where Delato is off (<see cref="DelatoSwitch"/>) nothing is created or wired, and an existing Delato site loses its
/// aliases, so its folder answers 404; its pages, users and data stay. Every step changes only what differs, so a
/// wired site is left alone.
/// </summary>
public sealed class DelatoSiteReconciler(IServiceProvider services, IServiceScopeFactory scopes, IConfiguration configuration,
    ILogger<DelatoSiteReconciler> logger) : ISiteContentReconciler
{
    public const string SiteKeySetting = "Delato:SiteKey";
    public const string SiteKey = "delato";
    public const string SiteName = "Delato";
    private static readonly string[] RemovedPaths = { "private", "mypage" };

    /// <summary>The ledger module's definition name, as Stomme registers it.</summary>
    internal static readonly string LedgerModule = typeof(Delato.Module.Ledger.Index).ToModuleDefinitionName();

    /// <summary>The site's languages: English, the default, and Persian.</summary>
    internal static readonly (string Code, bool IsDefault)[] Languages = { ("en", true), ("fa", false) };

    public string Name => "Delato site";

    public async Task ReconcileAsync(SiteContentReconciliationContext context, CancellationToken ct = default)
    {
        // the host's scope for this site: its tenant and site are set
        var settings = services.GetRequiredService<ISettingRepository>();
        var site = context.Site;
        var off = services.GetService<DelatoSwitch>()?.IsOff != false;

        var ours = await settings.GetSettingValueAsync(EntityNames.Site, site.SiteId, SiteKeySetting, "") == SiteKey;
        List<Site>? live = null;
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
                await EnsureSiteAsync(context, live!);
                break;
            case Step.Hide:
                await HideAsync(live!);
                break;
        }
    }

    public enum Step { Nothing, Wire, Ensure, Hide }

    /// <summary>
    /// What a pass does. On the Delato site: wire it, unless Delato is off. On the host's root site: create the Delato
    /// site or give it back its aliases, or, off, take its aliases away. Elsewhere: nothing. Off, nothing is ever
    /// created or wired.
    /// </summary>
    internal static Step Decide(bool switchedOff, bool isOurSite, bool isRootSite) =>
        isOurSite ? (switchedOff ? Step.Nothing : Step.Wire)
        : isRootSite ? (switchedOff ? Step.Hide : Step.Ensure)
        : Step.Nothing;

    /// <summary>
    /// An off module's site answers nowhere: its aliases are removed, so a request to its folder resolves to the root
    /// site, which has no such page (404). The site, its pages, users and data stay; switching Delato on gives the site
    /// its aliases again on the next boot (<see cref="EnsureSiteAsync"/>).
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
                logger.LogInformation("Delato: off on this host, so alias {Alias} of site {SiteId} is removed; the site and its data stay.",
                    alias.Name, candidate.SiteId);
            }
        }
    }

    internal static List<Alias> AliasesOf(int siteId, IEnumerable<Alias> aliases) =>
        aliases.Where(a => a.SiteId == siteId).ToList();

    /// <summary>Creates the Delato site when it is missing, gives it every root alias + its folder, and wires it.</summary>
    private async Task EnsureSiteAsync(SiteContentReconciliationContext context, List<Site> live)
    {
        var settings = services.GetRequiredService<ISettingRepository>();
        var aliases = services.GetRequiredService<IAliasRepository>();

        Site? delato = null;
        foreach (var candidate in live)
        {
            if (await settings.GetSettingValueAsync(EntityNames.Site, candidate.SiteId, SiteKeySetting, "") == SiteKey)
            {
                delato = candidate;
                break;
            }
        }

        var allAliases = (await aliases.GetAliasesAsync()).Where(a => a.TenantId == context.Alias.TenantId).ToList();
        if (delato == null)
        {
            delato = await services.GetRequiredService<ISiteRepository>().AddSiteAsync(NewSite(context.Site));
            await settings.AddSettingAsync(new Setting
            {
                EntityName = EntityNames.Site, EntityId = delato.SiteId, SettingName = SiteKeySetting, SettingValue = SiteKey, IsPrivate = true,
            });
            logger.LogInformation("Created the Delato site {SiteId}.", delato.SiteId);
        }

        foreach (var root in allAliases.Where(a => a.SiteId == context.Site.SiteId))
        {
            var name = root.Name + "/" + DelatoSite.Folder;
            var existing = allAliases.FirstOrDefault(a => a.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                await aliases.AddAliasAsync(new Alias { Name = name, TenantId = context.Alias.TenantId, SiteId = delato.SiteId, IsDefault = root.IsDefault });
                logger.LogInformation("Added alias {Alias} to the Delato site {SiteId}.", name, delato.SiteId);
            }
            else if (existing.SiteId != delato.SiteId)
            {
                logger.LogWarning("Alias {Alias} belongs to site {Other}, not to the Delato site; left as it is.", name, existing.SiteId);
            }
        }

        var siteAliases = (await aliases.GetAliasesAsync()).Where(a => a.SiteId == delato.SiteId).ToList();
        var alias = siteAliases.FirstOrDefault(a => a.IsDefault) ?? siteAliases.FirstOrDefault();
        if (alias == null)
        {
            logger.LogWarning("The Delato site has no alias yet, so it was not wired.");
            return;
        }

        // A scope of its own, routed to the Delato site, inside the tenant scope the host already entered.
        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ITenantManager>().SetAliasAsync(alias);
        var fresh = await scope.ServiceProvider.GetRequiredService<ISiteRepository>().GetSiteAsync(delato.SiteId);
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
        // shared expenses are an app of forms and live balances: interactive, on the server
        RenderMode = RenderModes.Interactive,
        Runtime = Runtimes.Server,
        Prerender = true,
        Hybrid = false,
        EnhancedNavigation = false,
        AllowRegistration = true,
        CaptureBrokenUrls = false,
        VisitorTracking = false,
        PwaIsEnabled = false,
    };

    private async Task WireAsync(IServiceProvider provider, Site site)
    {
        var pages = provider.GetRequiredService<IPageRepository>();

        // Home carries the ledger, for signed-in accounts only (anonymous visitors are sent to sign in); every other
        // module the template put there goes. Reading the definitions registers the ones in the loaded assemblies.
        var registered = (await provider.GetRequiredService<IModuleDefinitionRepository>().GetModuleDefinitionsAsync(site.SiteId))
            .Select(item => item.ModuleDefinitionName).ToHashSet();
        if (!registered.Contains(LedgerModule))
        {
            logger.LogError("Delato's ledger module is not registered on site {SiteId} ({Module}); Home is left as it is.", site.SiteId, LedgerModule);
        }
        else if ((await pages.GetPagesAsync(site.SiteId)).FirstOrDefault(item => item.Path == "") is { } home)
        {
            await EnsureOnlyModuleAsync(provider, site, home, LedgerModule, SiteName, SignedInOnly());
            if (!SamePermissions(home.PermissionList, SignedInOnly()))
            {
                home.PermissionList = SignedInOnly();
                await pages.UpdatePageAsync(home);
                logger.LogInformation("Delato: Home is for signed-in accounts only.");
            }
        }

        // the template's sample pages have no place here (soft-deleted pages still count as existing, so the
        // framework's template top-up does not bring them back)
        foreach (var page in (await pages.GetPagesAsync(site.SiteId)).Where(item => RemovedPaths.Contains(item.Path) && !item.IsDeleted))
        {
            page.IsDeleted = true;
            page.DeletedBy = SiteName;
            page.DeletedOn = DateTime.UtcNow;
            await pages.UpdatePageAsync(page);
            logger.LogInformation("Delato: page /{Path} deleted.", page.Path);
        }

        // Stomme's own stylesheet lays out the account forms (10.75.4); a head link an earlier version added is taken off.
        if (site.Name != SiteName || !site.AllowRegistration || !string.IsNullOrEmpty(site.HeadContent))
        {
            site.Name = SiteName;
            site.AllowRegistration = true;
            site.HeadContent = "";
            await provider.GetRequiredService<ISiteRepository>().UpdateSiteAsync(site);
            logger.LogInformation("Delato: site {SiteId} named and open to registration.", site.SiteId);
        }

        var languages = provider.GetRequiredService<ILanguageRepository>();
        foreach (var language in LanguageChanges(site.SiteId, await languages.GetLanguagesAsync(site.SiteId)))
        {
            if (language.LanguageId == 0)
            {
                await languages.AddLanguageAsync(language);
            }
            else
            {
                await languages.UpdateLanguageAsync(language);
            }
            logger.LogInformation("Delato: language {Code} set on site {SiteId} (default: {IsDefault}).", language.Code, site.SiteId, language.IsDefault);
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
            logger.LogWarning("Delato: no Delato:Mail settings on this host, so a new account's confirmation e-mail cannot be sent.");
        }

        // Delato's app signs in at this site's own authority, as a client limited to this site
        await AppClient.EnsureAsync(provider, site.SiteId, logger);
    }

    /// <summary>Places the module on the page once, with these permissions, and takes every other placement off it.</summary>
    private async Task EnsureOnlyModuleAsync(IServiceProvider provider, Site site, Page page, string moduleDefinition, string title, List<Permission> permissions)
    {
        var modules = provider.GetRequiredService<IModuleRepository>();
        var pageModules = provider.GetRequiredService<IPageModuleRepository>();

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
            logger.LogInformation("Delato: {Module} placed on /{Path}.", moduleDefinition, page.Path);
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
            logger.LogInformation("Delato: {Module} taken off /{Path}.", other.Module?.ModuleDefinitionName, page.Path);
        }
    }

    /// <summary>Signed-in accounts view; the site's Administrators view and edit.</summary>
    internal static List<Permission> SignedInOnly() => new()
    {
        new Permission(PermissionNames.View, RoleNames.Registered, true),
        new Permission(PermissionNames.View, RoleNames.Admin, true),
        new Permission(PermissionNames.Edit, RoleNames.Admin, true),
    };

    internal static bool SamePermissions(IEnumerable<Permission>? current, IEnumerable<Permission> wanted)
    {
        static string Key(Permission item) => $"{item.PermissionName}|{item.RoleName}|{item.UserId}|{item.IsAuthorized}";
        return current != null && current.Select(Key).ToHashSet().SetEquals(wanted.Select(Key));
    }

    /// <summary>
    /// The languages to add (id 0) or change so the site has exactly <see cref="Languages"/>' defaults: English the
    /// default, Persian beside it. A language the site has beyond these is left as it is; an extra default loses it.
    /// </summary>
    internal static List<Language> LanguageChanges(int siteId, IEnumerable<Language> current)
    {
        var existing = current.ToList();
        var changes = new List<Language>();
        foreach (var (code, isDefault) in Languages)
        {
            var language = existing.FirstOrDefault(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase));
            if (language is null)
            {
                changes.Add(new Language { SiteId = siteId, Code = code, IsDefault = isDefault });
            }
            else if (language.IsDefault != isDefault)
            {
                language.IsDefault = isDefault;
                changes.Add(language);
            }
        }
        foreach (var other in existing.Where(l => l.IsDefault && !Languages.Any(w => string.Equals(w.Code, l.Code, StringComparison.OrdinalIgnoreCase))))
        {
            other.IsDefault = false;
            changes.Add(other);
        }
        return changes;
    }

    /// <summary>The site settings Delato keeps: an account confirms its e-mail before it can sign in, and the mail settings.</summary>
    internal static List<(string Name, string Value)> SiteSettings(IConfiguration configuration)
    {
        var all = new List<(string, string)> { ("LoginOptions:RequireConfirmedEmail", "true") };
        all.AddRange(MailSettings(configuration));
        return all;
    }

    /// <summary>
    /// Stomme's SMTP settings for the site from <c>Delato:Mail:Host</c>, <c>Port</c> (25), <c>Sender</c> and <c>Ssl</c>
    /// (None): plain SMTP to the host's relay, which takes no credential. None when Host or Sender is missing.
    /// </summary>
    internal static List<(string Name, string Value)> MailSettings(IConfiguration configuration)
    {
        var host = configuration["Delato:Mail:Host"];
        var sender = configuration["Delato:Mail:Sender"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(sender))
        {
            return new();
        }
        return new()
        {
            ("SMTPEnabled", "True"),
            ("SMTPHost", host.Trim()),
            ("SMTPPort", string.IsNullOrWhiteSpace(configuration["Delato:Mail:Port"]) ? "25" : configuration["Delato:Mail:Port"]!.Trim()),
            ("SMTPSSL", string.IsNullOrWhiteSpace(configuration["Delato:Mail:Ssl"]) ? "None" : configuration["Delato:Mail:Ssl"]!.Trim()),
            ("SMTPAuthentication", "Basic"),
            ("SMTPSender", sender.Trim()),
        };
    }

    private static async Task EnsureSettingAsync(ISettingRepository settings, Site site, string name, string value)
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
    /// Delato switches it on, as the Lek module does. A site without SMTP settings is skipped by the job.
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
            logger.LogInformation("Delato: the notification job is on, for the site's confirmation e-mail.");
        }
        else if (!job.IsEnabled)
        {
            job.IsEnabled = true;
            await jobs.UpdateJobAsync(job);
            logger.LogInformation("Delato: the notification job is on, for the site's confirmation e-mail.");
        }
    }
}
