using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Didin.Server.Services
{
    /// <summary>
    /// On a host that switched Didin off, Didin's controllers are not there at all: they leave MVC's application model, so
    /// no endpoint is mapped for them and every Didin API address answers 404. The host's authorization runs on the chosen
    /// endpoint before any MVC filter, so a guard filter alone would let an [Authorize] controller answer 401 or 403 and
    /// tell a caller the API exists.
    /// </summary>
    public sealed class DidinControllersOff : IConfigureOptions<MvcOptions>
    {
        private readonly DidinSwitch @switch;

        public DidinControllersOff(DidinSwitch @switch) => this.@switch = @switch;

        public void Configure(MvcOptions options)
        {
            if (@switch.IsOff) options.Conventions.Add(new Removal());
        }

        /// <summary>Takes every controller of this module's assembly out of the application model.</summary>
        internal sealed class Removal : IApplicationModelConvention
        {
            public void Apply(ApplicationModel application)
            {
                foreach (var controller in application.Controllers.Where(c => c.ControllerType.Assembly == typeof(DidinSwitch).Assembly).ToList())
                {
                    application.Controllers.Remove(controller);
                }
            }
        }
    }
}
