#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Stomme.SharedKernel.Domain;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// The articles' closed repository over the host's storage-blind store. Every read is one SITE's,
    /// filtered on the site id (an int, the filter every store answers alike); the slug is matched in memory, after
    /// that filter, so no store's string comparison is involved.
    /// </summary>
    public interface IArticleRepository : IRepository<Article, Guid>
    {
        Task<IReadOnlyList<Article>> ListForSiteAsync(int siteId, CancellationToken ct = default);
    }

    public sealed class ArticleRepository(IRepository<Article, Guid> inner) : IArticleRepository
    {
        public Task<Article?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);

        public Task<IReadOnlyList<Article>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);

        public Task AddAsync(Article aggregate, CancellationToken ct = default) => inner.AddAsync(aggregate, ct);

        public Task UpdateAsync(Article aggregate, CancellationToken ct = default) => inner.UpdateAsync(aggregate, ct);

        public Task DeleteAsync(Article aggregate, CancellationToken ct = default) => inner.DeleteAsync(aggregate, ct);

        public Task<IReadOnlyList<Article>> FindAsync(Expression<Func<Article, bool>> predicate, CancellationToken ct = default) => inner.FindAsync(predicate, ct);

        public async Task<IReadOnlyList<Article>> ListForSiteAsync(int siteId, CancellationToken ct = default) =>
            (await inner.FindAsync(article => article.SiteId == siteId, ct)).ToList();
    }
}
