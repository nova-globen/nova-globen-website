#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Stomme.Tenancy.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using SharedKernelUnitOfWork = Stomme.SharedKernel.Domain.IUnitOfWork;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// Brings a site's articles in line with the repository: an article whose source is new is created,
    /// one whose source changed is re-rendered, one whose source is gone is retired; a draft is not published. Idempotent:
    /// a boot with no change in content/articles writes nothing.
    /// </summary>
    public sealed class ArticleImport(
        IArticleRepository articles,
        SharedKernelUnitOfWork unitOfWork,
        TimeProvider clock,
        ILogger<ArticleImport> logger)
    {
        /// <summary>Imports, and answers the site's published articles, newest first.</summary>
        public async Task<IReadOnlyList<ArticleSummary>> ImportAsync(TenantId tenantId, int siteId, CancellationToken ct = default)
        {
            var sources = ArticleSource.LoadAll(ArticleAssets.Exists).Where(s => !s.Draft).ToList();
            var existing = (await articles.ListForSiteAsync(siteId, ct)).ToDictionary(a => a.Slug, StringComparer.Ordinal);
            var now = clock.GetUtcNow().UtcDateTime;
            int created = 0, updated = 0, retired = 0;

            foreach (var source in sources)
            {
                if (!existing.TryGetValue(source.Slug, out var article))
                {
                    await articles.AddAsync(Article.Import(Guid.NewGuid(), tenantId, siteId, source, ArticleRenderer.Render(source.Markdown), now), ct);
                    created++;
                }
                else if (article.SourceHash != source.Hash || article.IsRetired || article.CoverPng != source.CoverPng || article.CoverWebp != source.CoverWebp)
                {
                    article.Apply(source, ArticleRenderer.Render(source.Markdown), now);
                    await articles.UpdateAsync(article, ct);
                    updated++;
                }
            }

            var current = new HashSet<string>(sources.Select(s => s.Slug), StringComparer.Ordinal);
            foreach (var gone in existing.Values.Where(a => !a.IsRetired && !current.Contains(a.Slug)))
            {
                gone.Retire(now);
                await articles.UpdateAsync(gone, ct);
                retired++;
            }

            await unitOfWork.SaveChangesAsync(ct);
            if (created + updated + retired > 0)
            {
                logger.LogInformation("Articles of site {SiteId}: {Created} created, {Updated} updated, {Retired} retired, {Total} published.",
                    siteId, created, updated, retired, sources.Count);
            }

            return (await articles.ListForSiteAsync(siteId, ct))
                .Where(a => !a.IsRetired)
                .OrderByDescending(a => a.PublishedOn).ThenBy(a => a.Slug, StringComparer.Ordinal)
                .Select(ServerArticleService.ToSummary)
                .ToList();
        }
    }
}
