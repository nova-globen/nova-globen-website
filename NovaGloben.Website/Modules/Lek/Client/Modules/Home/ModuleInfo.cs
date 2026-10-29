using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.Home
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Games",
            Description = "The list of games on this site",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
