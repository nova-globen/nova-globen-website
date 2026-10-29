using System.Collections.Generic;
using Stomme.Models;
using Stomme.Themes;

namespace NovaGloben.Website.Client.Themes.NovaGloben
{
    /// <summary>
    /// The Nova Globen theme: the brand colours mapped onto Stomme's --db-* tokens with the contrast fixes, self-hosted
    /// Inter, and the shell both public sites share. Its static files are this Client project's web
    /// assets under wwwroot/Themes/{namespace}/, which Stomme serves at /Themes/{namespace}/ (a "~/" resource URL).
    /// </summary>
    public class ThemeInfo : ITheme
    {
        public Theme Theme => new Theme
        {
            Name = "Nova Globen",
            Version = "1.0.0",
            PackageName = "NovaGloben.Website.Client",
            // The theme reads its options from site and page settings (see ThemeOptions); it has no settings control.
            ThemeSettingsType = "",
            ContainerSettingsType = "",
            Resources = new List<Resource>()
            {
                new Stylesheet("~/theme.css")
            }
        };
    }
}
