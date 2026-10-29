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
                // Cyborg default); Theme.css carries the Fluent token layer.
                new Stylesheet(
                    "https://cdnjs.cloudflare.com/ajax/libs/twitter-bootstrap/5.3.8/css/bootstrap.min.css",
                    "sha512-2bBQCjcnw658Lho4nlXJcc6WkV/UxpE/sAokbXPxQNGqmNdQrWqtw26Ns9kFF/yG792pKR1Sx8/Y1Lf1XN4GKA==",
                    "anonymous"),
                // the theme's own layer, addressed by its content hash (DidinAssets); the design's typeface, Poppins, is used
                // when the device has it, and system fonts otherwise: no font is fetched from another site
                new Stylesheet(DidinAssets.Url(DidinAssets.Theme)),
                new Script(
                    "https://cdnjs.cloudflare.com/ajax/libs/twitter-bootstrap/5.3.8/js/bootstrap.bundle.min.js",
                    "sha512-HvOjJrdwNpDbkGJIG2ZNqDlVqMo77qbs4Me4cah0HoDrfhrbA+8SBlZn1KrvAQw7cILLPFJvdwIgphzQmMm+Pw==",
                    "anonymous")
            }
        };
    }
}
