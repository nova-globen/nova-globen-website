using System.Collections.Generic;
using Stomme.Models;
using Stomme.Modules;
using Stomme.Shared;

namespace Didin.Module.Login
{
    /// <summary>
    /// Fluent UI replacement for the stock Stomme login form. The
    /// credential flow is unchanged: UserService.LoginUserAsync validates, then
    /// a form post-back to /pages/login/ sets the auth cookie (same as
    /// Stomme.Modules.Admin.Login). External login / 2FA / passkeys / password
    /// recovery are intentionally omitted - unused on this personal site.
    /// </summary>
    public class ModuleInfo : IModule
    {
        public ModuleDefinition ModuleDefinition => new ModuleDefinition
        {
            Name = "Login",
            Description = "Didin sign-in page - Fluent UI login form",
            Version = "1.0.0",
            PackageName = "Didin.Client.Stomme",
            ServerManagerType = "",
            // the sign-in screen's styles live with the app's
            Resources = new List<Resource>
            {
                new Stylesheet(DidinAssets.Url(DidinAssets.Module)),
            },
        };
    }
}
