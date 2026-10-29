using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.Elementals
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Elementals",
            Description = "Elementals - 2D arena PvP, 1v1 to 3v3, skill decides",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
