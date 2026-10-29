using Stomme.Models;
using Stomme.Modules;

namespace Delato.Module.Ledger;

public sealed class ModuleInfo : IModule
{
    public ModuleDefinition ModuleDefinition => new()
    {
        Name = "Delato",
        Description = "Shared expenses: groups, couples and subgroups, expenses in any currency, and who pays whom",
        Version = "1.0.0",
        PackageName = "Delato.Client.Stomme",
        ServerManagerType = "",
        Resources = new List<Resource>
        {
            new Stylesheet("_content/Delato.Client.Stomme/Module.css"),
        },
    };
}
