using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.InsideJob
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Inside Job",
            Description = "Inside Job - design a safehouse the solver proves beatable, then raid everyone else's",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
