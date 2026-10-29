using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.Extensions.Options;

namespace Lek.Server.Services
{
    /// <summary>
    /// On a host that switched Lek off, Lek's controllers are not there at all: they leave MVC's application model, so
    /// no endpoint is mapped for them and every Lek API address answers 404. The host's authorization runs on the chosen
    /// endpoint before any MVC filter, so a guard filter alone would let an [Authorize] controller answer 401 or 403 and
    /// tell a caller the API exists.
    /// </summary>
    public sealed class LekControllersOff : IConfigureOptions<MvcOptions>
    {
        private readonly LekSwitch @switch;

        public LekControllersOff(LekSwitch @switch) => this.@switch = @switch;

        public void Configure(MvcOptions options)
        {
            if (@switch.IsOff) options.Conventions.Add(new Removal());
        }

        /// <summary>Takes every controller of this module's assembly out of the application model.</summary>
        internal sealed class Removal : IApplicationModelConvention
        {
            public void Apply(ApplicationModel application)
            {
                foreach (var controller in application.Controllers.Where(c => c.ControllerType.Assembly == typeof(LekSwitch).Assembly).ToList())
                {
                    application.Controllers.Remove(controller);
                }
            }
        }
    }
}
