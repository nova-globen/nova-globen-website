using Stomme.Models;
using Stomme.Modules;

namespace Delato.Module.Privacy;

public sealed class ModuleInfo : IModule
{
    public ModuleDefinition ModuleDefinition => new()
    {
        Name = "Delato privacy notice",
        Description = "What the Delato site keeps about its members, and for how long",
        Version = "1.0.0",
        PackageName = "Delato.Client.Stomme",
        ServerManagerType = "",
    };
}
