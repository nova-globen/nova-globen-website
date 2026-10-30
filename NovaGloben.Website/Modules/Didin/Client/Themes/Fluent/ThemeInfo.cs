using System.Collections.Generic;
using Stomme.Models;
using Stomme.Shared;
using Stomme.Themes;

namespace Didin.Theme.Fluent
{
    /// <summary>
    /// Didin Fluent - the site's design system. Microsoft Fluent
    /// Design tokens (dark) applied over vanilla Bootstrap, so Stomme's own
    /// components (menu, profile, control panel, admin pages) keep working
    /// while the whole site speaks one design language. This replaces
    /// the framework's stock theme and its Bootswatch layer.
    /// </summary>
    public class ThemeInfo : ITheme
    {
        public Stomme.Models.Theme Theme => new Stomme.Models.Theme
        {
            Name = "Didin Fluent",
            Version = "1.0.0",
            PackageName = "Didin.Client.Stomme",
            // dark-only: Stomme renders every page dark from the first paint - FluentUI, Bootstrap and
            // the browser's own widgets included - whatever the visitor's system mode, and hides the colour toggle
            ColorMode = ThemeColorModes.Dark,
            Resources = new List<Resource>
            {
                // vanilla Bootstrap as structural base (NOT the framework's Bootswatch
                // Cyborg default); Theme.css carries the Fluent token layer. Served from this site, not a CDN, so a
                // visitor's browser makes no request to another host (the files are cdnjs's 5.3.8, byte for byte).
                new Stylesheet(DidinAssets.Url(DidinAssets.BootstrapCss)),
                // the theme's own layer, addressed by its content hash (DidinAssets); the design's typeface, Poppins, is used
                // when the device has it, and system fonts otherwise: no font is fetched from another site
                new Stylesheet(DidinAssets.Url(DidinAssets.Theme)),
                new Script(DidinAssets.Url(DidinAssets.BootstrapJs))
            }
        };
    }
}
