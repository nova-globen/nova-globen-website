using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.Privacy
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Lek privacy notice",
            Description = "What the Lek site keeps about its players, and for how long",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
