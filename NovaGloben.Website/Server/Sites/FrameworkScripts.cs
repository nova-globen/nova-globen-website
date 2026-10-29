#nullable enable
using System.Text.RegularExpressions;

namespace NovaGloben.Website.Sites
{
    /// <summary>
    /// Takes Stomme's client framework scripts out of the pages an anonymous visitor receives. Stomme's
    /// App.razor always ends the body with blazor.web.js, js/app.js, js/loadjs.min.js, js/interop.js and a
    /// &lt;page-script&gt; for js/reload.js; blazor.web.js in turn loads FluentUI's and Stomme's JS modules. They serve
    /// enhanced navigation and interactive components, and these sites use neither: every public page is static server
    /// rendering with enhanced navigation off, and the contact form is a plain HTML form. Measured 2026-10-23
    /// (Lighthouse mobile, an article): about 200 KB of script a visitor downloaded for nothing, competing with the page's
    /// largest image.
    ///
    /// Only these exact elements are removed; a script a page or module adds is kept. A signed-in editor's pages are never
    /// buffered (<see cref="SecurityHeaders.IsVisitorPage"/>), so the admin keeps everything. When Stomme can leave them
    /// out itself for a static site, this goes.
    /// </summary>
    public static class FrameworkScripts
    {
        private static readonly Regex Pattern = new(
            @"[ \t]*(?:<script src=""(?:_framework/blazor\.web\.js|js/(?:app|loadjs\.min|interop)\.js(?:\?v=[A-Za-z0-9]+)?)""></script>" +
            @"|<page-script src=""\./js/reload\.js(?:\?v=[A-Za-z0-9]+)?""></page-script>)\r?\n?",
            RegexOptions.Compiled);

        /// <summary>The page without Stomme's framework scripts.</summary>
        public static string Remove(string html) => Pattern.Replace(html, "");
    }
}
