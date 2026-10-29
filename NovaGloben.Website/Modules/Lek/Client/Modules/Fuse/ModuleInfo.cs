using Stomme.Models;
using Stomme.Modules;

namespace Lek.Module.Fuse
{
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Fuse",
            Description = "Fuse - a multiplayer hot-potato game with a bomb and a song that stops at random",
            Version = "0.1.0",
            ServerManagerType = ""
        };
    }
}
