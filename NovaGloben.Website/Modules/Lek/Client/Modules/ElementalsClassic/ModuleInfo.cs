using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.ElementalsClassic
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Elementals (classic art)",
            Description = "Elementals with its first art, kept to compare",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
