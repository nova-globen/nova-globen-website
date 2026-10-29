using Stomme.Models;
using Stomme.Modules;

namespace NovaGloben.Website.Client.Modules.Articles
{
    /// <summary>
    /// The articles: imported from the repository's markdown on every boot, stored in the module's
    /// storage-blind, tenant-owned store, and shown as a paged list filtered by tag, as the newest few, or one at a time
    /// (the module's NovaGloben:ArticlesView setting).
    /// </summary>
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Articles",
            Description = "The website's articles, imported from the repository's content/articles folder.",
            Version = "1.0.0",
            Categories = "Common",
            Owner = "Nova Globen AB",
            PackageName = "NovaGloben.Website.Client",
            Dependencies = "NovaGloben.Website.Shared.Stomme"
        };
    }
}
