using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.Noir
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Bullet Noir",
            Description = "Bullet Noir - a 3D shootout with bullet time and shootdodge",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
