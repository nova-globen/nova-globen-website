#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Stomme.Infrastructure;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// The server's <see cref="IArticleService"/>: the published (not retired) articles of the site the request was
    /// routed to, newest first. Static pages render with it; the controller answers a browser with it.
    /// </summary>
    public sealed class ServerArticleService(IArticleRepository articles, ITenantManager tenants) : IArticleService
    {
        public const int MaxPageSize = 50;

        public async Task<ArticleListPage> ListAsync(int page, int pageSize, string tag)
        {
            pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
            var all = await PublishedAsync();
            var tags = all.SelectMany(a => a.Tags)
                .GroupBy(t => t, StringComparer.Ordinal)
                .Select(g => new TagCount { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(t => t.Count).ThenBy(t => t.Tag, StringComparer.Ordinal)
                .ToList();

            tag = (tag ?? string.Empty).Trim().ToLowerInvariant();
            var filtered = tag.Length == 0 ? all : all.Where(a => a.Tags.Contains(tag, StringComparer.Ordinal)).ToList();
            var pageCount = Math.Max(1, (int)Math.Ceiling(filtered.Count / (double)pageSize));
            page = Math.Clamp(page, 1, pageCount);

            return new ArticleListPage
            {
                Items = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList(),
                Page = page,
                PageCount = pageCount,
                Total = filtered.Count,
                Tag = tag,
                Tags = tags
            };
        }

        public async Task<List<ArticleSummary>> LatestAsync(int count) =>
            (await PublishedAsync()).Take(Math.Clamp(count, 1, MaxPageSize)).ToList();

        public async Task<ArticleDetail> GetAsync(string slug)
        {
            var siteId = await SiteIdAsync();
            var wanted = (slug ?? string.Empty).Trim().ToLowerInvariant();
            var article = (await articles.ListForSiteAsync(siteId)).FirstOrDefault(a => !a.IsRetired && a.Slug == wanted);
            return article == null ? null! : ToDetail(article);
        }

        private async Task<List<ArticleSummary>> PublishedAsync()
        {
            var siteId = await SiteIdAsync();
            return (await articles.ListForSiteAsync(siteId))
                .Where(a => !a.IsRetired)
                .OrderByDescending(a => a.PublishedOn).ThenBy(a => a.Slug, StringComparer.Ordinal)
                .Select(ToSummary)
                .ToList();
        }

        private async Task<int> SiteIdAsync() => (await tenants.GetAliasAsync())?.SiteId ?? -1;

        internal static ArticleSummary ToSummary(Article a) => Fill(new ArticleSummary(), a);

        private static ArticleDetail ToDetail(Article a)
        {
            var detail = Fill(new ArticleDetail(), a);
            detail.Html = a.Html;
            return detail;
        }

        private static T Fill<T>(T model, Article a) where T : ArticleSummary
        {
            model.Slug = a.Slug;
            model.Title = a.Title;
            model.Summary = a.Summary;
            model.Tags = a.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();
            model.PublishedOn = a.PublishedOn;
            model.CoverPng = a.CoverPng;
            model.CoverWebp = a.CoverWebp;
            return model;
        }
    }
}
