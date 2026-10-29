using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.SilentHour
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Silent Hour",
            Description = "Silent Hour - the daily stealth-tactics puzzle",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
