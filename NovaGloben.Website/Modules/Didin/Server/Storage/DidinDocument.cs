#nullable enable
using System;
using Stomme.Persistence.Abstractions;
using Stomme.SharedKernel.Domain;
using Stomme.Tenancy.Domain;

namespace Didin.Server.Storage
{
    /// <summary>The kinds of document Didin keeps. Stored as a number; never renumber one.</summary>
    public enum DidinKind
    {
        Title = 1,
        Episode = 2,
        File = 3,
        Collection = 4,
        CatalogVersion = 5,
        Poster = 6,
        EmbeddedSubtitle = 7,
        UserState = 8,
        Rating = 9,
        ReviewVerdict = 10,
    }

    /// <summary>
    /// One of Didin's documents on one site: a catalogue title, an episode, a file, a collection, the catalogue's version,
    /// a poster's or a subtitle track's metadata, a user's own state, a rating or a review verdict. The document itself is
    /// JSON; the row carries what reads filter on, all of it numbers, which every store compares alike.
    /// </summary>
    /// <remarks>
    /// One aggregate for every kind, rather than one per kind with a column per field: the catalogue is read whole into
    /// memory, so no read needs a field as a column, and the module store never renames or drops a column - a document's
    /// shape can change without leaving dead columns behind. Tenant-owned and site-scoped: every read filters by
    /// <see cref="SiteId"/> as well, because the tenant filter does not separate sites.
    /// </remarks>
    public sealed class DidinDocument : AggregateRoot<Guid>, ITenantOwned
    {
        // Materialisation constructor - every store builds the aggregate through it. Not for application use.
        private DidinDocument()
        {
            TenantId = null!;
            Json = string.Empty;
        }

        public TenantId TenantId { get; private set; }

        public int SiteId { get; private set; }

        public DidinKind Kind { get; private set; }

        /// <summary>
        /// The document's number within its kind: a title's, episode's, file's or collection's catalogue number, the
        /// title a poster or rating is for, the file a verdict or subtitle track is for, the user a state belongs to; 0
        /// for the catalogue's version.
        /// </summary>
        public int Number { get; private set; }

        /// <summary>The second part of the document's identity where it has one (a rating's user, a track's stream); else 0.</summary>
        public int Owner { get; private set; }

        public string Json { get; private set; }

        public DateTime UpdatedUtc { get; private set; }

        public static DidinDocument Create(Guid id, TenantId tenantId, int siteId, DidinKind kind, int number, int owner, string json, DateTime nowUtc)
        {
            ArgumentNullException.ThrowIfNull(tenantId);
            if (siteId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(siteId), "A Didin document belongs to a site.");
            }
            return new DidinDocument
            {
                Id = id,
                TenantId = tenantId,
                SiteId = siteId,
                Kind = kind,
                Number = number,
                Owner = owner,
                Json = json ?? string.Empty,
                UpdatedUtc = nowUtc,
            };
        }

        /// <summary>Replaces the document's content.</summary>
        public void Replace(string json, DateTime nowUtc)
        {
            Json = json ?? string.Empty;
            UpdatedUtc = nowUtc;
        }
    }
}
