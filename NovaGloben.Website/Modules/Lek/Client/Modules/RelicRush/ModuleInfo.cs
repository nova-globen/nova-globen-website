using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.RelicRush
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Relic Rush",
            Description = "Relic Rush - team relic carrying in a temple maze",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
