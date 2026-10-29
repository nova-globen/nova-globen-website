using System;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.FileProviders;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// The articles' covers and images: the files beside each content/articles/&lt;slug&gt;/index.md, copied by the build to
    /// articles/ beside the app (development and published alike) and served at /blog/&lt;slug&gt;/&lt;file&gt; - the paths
    /// the old site published them at, which cross-posted copies of the articles load.
    /// </summary>
    public static class ArticleAssets
    {
        public static readonly string Folder = Path.Combine(AppContext.BaseDirectory, "articles");

        /// <summary>Whether a web path such as "blog/&lt;slug&gt;/cover.png" is one of the served files.</summary>
        public static bool Exists(string webPath)
        {
            const string prefix = "blog/";
            var relative = webPath.TrimStart('/');
            if (!relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return File.Exists(Path.Combine(Folder, relative.Substring(prefix.Length).Replace('/', Path.DirectorySeparatorChar)));
        }

        public static void Serve(IApplicationBuilder app)
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }
            app.UseStaticFiles(new StaticFileOptions
            {
                RequestPath = "/blog",
                FileProvider = new PhysicalFileProvider(Folder),
                OnPrepareResponse = context => context.Context.Response.Headers.CacheControl = "public, max-age=86400"
            });
        }
    }
}
