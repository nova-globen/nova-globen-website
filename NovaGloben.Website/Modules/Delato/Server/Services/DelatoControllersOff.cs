using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Delato.Server.Services;

/// <summary>
/// On a host where Delato is off, Delato's controllers are not there at all: they leave MVC's application model, so no
/// endpoint is mapped for them and every Delato API address answers 404. The host's authorization runs on the chosen
/// endpoint before any MVC filter, so a guard filter alone would let an [Authorize] controller answer 401 and tell a
/// caller the API exists.
/// </summary>
public sealed class DelatoControllersOff(DelatoSwitch @switch) : IConfigureOptions<MvcOptions>
{
    public void Configure(MvcOptions options)
    {
        if (@switch.IsOff) options.Conventions.Add(new Removal());
    }

    /// <summary>Takes every controller of this module's assembly out of the application model.</summary>
    internal sealed class Removal : IApplicationModelConvention
    {
        public void Apply(ApplicationModel application)
        {
            foreach (var controller in application.Controllers.Where(c => c.ControllerType.Assembly == typeof(DelatoSwitch).Assembly).ToList())
            {
                application.Controllers.Remove(controller);
            }
        }
    }
}
