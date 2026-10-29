using Stomme.Models;
using Stomme.Modules;

namespace NovaGloben.Website.Client.Modules.PageContent
{
    /// <summary>
    /// One section of a page, from the repository: the HTML file its NovaGloben:Content setting names, compiled into the
    /// app (content/sites/). The site reconciliation places it; nothing edits it in the admin pages.
    /// </summary>
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Page Content",
            Description = "A section of a page, from the website repository's content/sites folder.",
            Version = "1.0.0",
            Categories = "Common",
            Owner = "Nova Globen AB",
            PackageName = "NovaGloben.Website.Client",
            Dependencies = "NovaGloben.Website.Shared.Stomme"
        };
    }
}
