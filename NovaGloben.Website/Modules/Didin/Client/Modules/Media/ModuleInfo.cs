using System.Collections.Generic;
using Stomme.Models;
using Stomme.Modules;
using Stomme.Shared;

namespace Didin.Module.Media
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Media",
            Description = "Didin media library - browse, filter and play movies and series",
            Version = "1.0.0",
            PackageName = "Didin.Client.Stomme",
            ServerManagerType = "",
            Resources = new List<Resource>
            {
                new Stylesheet(DidinAssets.Url(DidinAssets.Module)),
            },
        };
    }
}
