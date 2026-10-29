using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.Spark
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Spark",
            Description = "Spark - a 2D arena brawler: pick a character, aim, win on skill",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
