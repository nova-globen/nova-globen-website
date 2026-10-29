using System.Collections.Generic;
using System.Threading.Tasks;
using Stomme.Infrastructure;
using Stomme.Models;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// The site template of every Nova Globen site (Installation:SiteTemplate): it creates no page, because the site's
    /// pages come from the repository and SiteReconciler creates them on the same boot, and on every boot after.
    /// </summary>
    public class RepositorySiteTemplate : ISiteTemplate
    {
        public string Name => "Nova Globen (from the repository)";

        public Task<List<PageTemplate>> CreateSiteAsync(Site site) => Task.FromResult(new List<PageTemplate>());
    }
}
