#nullable enable
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using Stomme.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// The articles as an Atom feed at /blog/feed.xml: the newest 50, with title, address, date,
    /// summary and tags - the same as the article list shows. Answered on a site that has articles, else 404.
    /// </summary>
    public static class ArticleFeed
    {
        public const string Path = "/blog/feed.xml";

        public static async Task InvokeAsync(HttpContext context, Func<Task> next)
        {
            if (!HttpMethods.IsGet(context.Request.Method) || !string.Equals(context.Request.Path.Value, Path, StringComparison.OrdinalIgnoreCase))
            {
                await next();
                return;
            }

            var alias = await context.RequestServices.GetRequiredService<ITenantManager>().GetAliasAsync();
            var articles = alias == null || !string.IsNullOrEmpty(alias.Path)
                ? new System.Collections.Generic.List<ArticleSummary>()
                : await context.RequestServices.GetRequiredService<IArticleService>().LatestAsync(50);
            if (articles.Count == 0)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var origin = $"{context.Request.Scheme}://{context.Request.Host}";
            context.Response.ContentType = "application/atom+xml; charset=utf-8";
            await context.Response.WriteAsync(Write(origin, articles), Encoding.UTF8);
        }

        public static string Write(string origin, System.Collections.Generic.IReadOnlyList<ArticleSummary> articles)
        {
            const string atom = "http://www.w3.org/2005/Atom";
            var builder = new StringBuilder();
            using (var writer = XmlWriter.Create(new StringWriter(builder), new XmlWriterSettings { Indent = true, OmitXmlDeclaration = false }))
            {
                writer.WriteStartDocument();
                writer.WriteStartElement("feed", atom);
                writer.WriteElementString("title", atom, "Nova Globen AB - Insights");
                writer.WriteElementString("subtitle", atom, "Technical articles on .NET, Azure and software engineering.");
                writer.WriteElementString("id", atom, origin + "/blog");
                writer.WriteElementString("updated", atom, Date(articles.Max(a => a.PublishedOn)));
                Link(writer, atom, origin + "/blog", null);
                Link(writer, atom, origin + Path, "self");
                writer.WriteStartElement("author", atom);
                writer.WriteElementString("name", atom, "Nova Globen AB");
                writer.WriteEndElement();

                foreach (var article in articles)
                {
                    writer.WriteStartElement("entry", atom);
                    writer.WriteElementString("title", atom, article.Title);
                    writer.WriteElementString("id", atom, origin + "/blog/" + article.Slug);
                    Link(writer, atom, origin + "/blog/" + article.Slug, null);
                    writer.WriteElementString("published", atom, Date(article.PublishedOn));
                    writer.WriteElementString("updated", atom, Date(article.PublishedOn));
                    writer.WriteElementString("summary", atom, article.Summary);
                    foreach (var tag in article.Tags)
                    {
                        writer.WriteStartElement("category", atom);
                        writer.WriteAttributeString("term", tag);
                        writer.WriteEndElement();
                    }
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                writer.WriteEndDocument();
            }
            return builder.ToString().Replace("encoding=\"utf-16\"", "encoding=\"utf-8\"");
        }

        private static void Link(XmlWriter writer, string ns, string href, string? rel)
        {
            writer.WriteStartElement("link", ns);
            if (rel != null)
            {
                writer.WriteAttributeString("rel", rel);
            }
            writer.WriteAttributeString("href", href);
            writer.WriteEndElement();
        }

        private static string Date(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }
}
