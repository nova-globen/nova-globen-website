#nullable enable
using System;
using Stomme.Persistence.Abstractions;
using Stomme.SharedKernel.Domain;
using Stomme.Tenancy.Domain;

namespace NovaGloben.Website.Articles
{
    /// <summary>
    /// An article of a site, imported from the repository's markdown (content/articles/&lt;slug&gt;/index.md) and rendered to
    /// HTML at import. The repository is its source: the import creates, updates and retires articles to match it, and
    /// nothing else writes them.
    /// </summary>
    /// <remarks>
    /// Storage-blind and tenant-owned: the host keeps one module store per installation and puts it
    /// on whichever database the installation chose; the tenant is stamped from the site's tenant when the article is
    /// created. Tags are kept as one comma-separated string so every store holds them as a plain column.
    /// </remarks>
    public sealed class Article : AggregateRoot<Guid>, ITenantOwned
    {
        // Materialisation constructor - every store builds the aggregate through it. Not for application use.
        private Article()
        {
            TenantId = null!;
            Slug = string.Empty;
            Title = string.Empty;
            Summary = string.Empty;
            Tags = string.Empty;
            Html = string.Empty;
            CoverPng = string.Empty;
            CoverWebp = string.Empty;
            SourceHash = string.Empty;
        }

        public TenantId TenantId { get; private set; }

        public int SiteId { get; private set; }

        /// <summary>Lower-case, as in the URL /blog/&lt;slug&gt;; unique per site.</summary>
        public string Slug { get; private set; }

        public string Title { get; private set; }

        public string Summary { get; private set; }

        /// <summary>Comma-separated, in the order the front matter lists them.</summary>
        public string Tags { get; private set; }

        public string Html { get; private set; }

        public string CoverPng { get; private set; }

        public string CoverWebp { get; private set; }

        /// <summary>The publication date from the front matter (a date, kept at midnight UTC).</summary>
        public DateTime PublishedOn { get; private set; }

        /// <summary>A hash of the markdown and of the renderer's version: the import rewrites an article only when it changes.</summary>
        public string SourceHash { get; private set; }

        public bool IsRetired { get; private set; }

        public DateTime UpdatedUtc { get; private set; }

        public static Article Import(Guid id, TenantId tenantId, int siteId, ArticleSource source, string html, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(tenantId);
            ArgumentNullException.ThrowIfNull(source);
            if (siteId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(siteId), "An article belongs to a site.");
            }

            var article = new Article { Id = id, TenantId = tenantId, SiteId = siteId, Slug = source.Slug };
            article.Apply(source, html, nowUtc);
            return article;
        }

        /// <summary>Brings the article in line with its source; the slug, and with it the address, never changes.</summary>
        public void Apply(ArticleSource source, string html, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(source);
            Title = source.Title;
            Summary = source.Summary;
            Tags = string.Join(",", source.Tags);
            Html = html ?? string.Empty;
            CoverPng = source.CoverPng;
            CoverWebp = source.CoverWebp;
            PublishedOn = source.PublishedOn;
            SourceHash = source.Hash;
            IsRetired = false;
            UpdatedUtc = nowUtc;
        }

        /// <summary>The source is gone from the repository: the article stops being listed and its page is retired.</summary>
        public void Retire(DateTime nowUtc)
        {
            IsRetired = true;
            UpdatedUtc = nowUtc;
        }
    }
}
