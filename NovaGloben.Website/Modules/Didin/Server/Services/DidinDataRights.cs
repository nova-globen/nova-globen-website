using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Storage;
using Stomme.Erasure;
using Stomme.Portability;

namespace Didin.Server.Services
{
    /// <summary>
    /// A person deleted from the Didin site (self-service or by an administrator): their own Didin data goes with them.
    /// Their list, progress, seen marks and subtitle choice (one user-state document) and their ratings and reviews are
    /// deleted; an administrator's verdict on a library file stays, since the catalogue's next import depends on it, but
    /// no longer names them. Idempotent: a second run finds nothing to do. Without this, Stomme's erasure would leave
    /// Didin's documents behind, since it cannot see inside them.
    /// </summary>
    public sealed class DidinUserErasure(DidinStore store) : ISubjectErasureParticipant
    {
        public const string UserStateStore = "Didin user state";
        public const string RatingsStore = "Didin ratings";
        public const string VerdictsStore = "Didin review verdicts";

        /// <summary>What a verdict names in place of the person who gave it.</summary>
        public const string ErasedName = "deleted account";

        public string Name => "Didin";

        public IReadOnlyList<ErasureDeclaration> Declarations { get; } = new[]
        {
            ErasureDeclaration.Delete(UserStateStore) with { Summary = "your Didin list, progress, seen titles and subtitle choice" },
            ErasureDeclaration.Delete(RatingsStore) with { Summary = "your Didin ratings and reviews" },
            ErasureDeclaration.Anonymise(VerdictsStore) with { Summary = "your username on Didin library verdicts (the verdicts stay)" },
        };

        public async Task<IReadOnlyList<ErasureOutcome>> EraseAsync(ErasureSubject subject, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(subject);
            var siteId = subject.SiteId;
            var userId = subject.UserId;
            if (siteId <= 0 || userId <= 0)
            {
                return new[]
                {
                    new ErasureOutcome(UserStateStore, ErasureHandling.Delete, 0),
                    new ErasureOutcome(RatingsStore, ErasureHandling.Delete, 0),
                    new ErasureOutcome(VerdictsStore, ErasureHandling.Anonymise, 0),
                };
            }

            var states = 0;
            if (await store.LoadAsync<UserStateDoc>(siteId, DidinKind.UserState, userId, ct: ct) != null)
            {
                await store.DeleteAsync(siteId, DidinKind.UserState, userId, ct: ct);
                states = 1;
            }

            var ratings = (await store.AllAsync<RatingDoc>(siteId, DidinKind.Rating, ct)).Where(r => r.UserId == userId).ToList();
            foreach (var rating in ratings)
            {
                await store.DeleteAsync(siteId, DidinKind.Rating, rating.TitleId, userId, ct);
            }

            var verdicts = 0;
            if (!string.IsNullOrEmpty(subject.Username))
            {
                foreach (var verdict in await store.AllAsync<ReviewVerdictDoc>(siteId, DidinKind.ReviewVerdict, ct))
                {
                    var fileNumber = CatalogQuery.NumberOf(verdict.FileId);
                    if (verdict.UserName != subject.Username || fileNumber == 0) continue;
                    verdict.UserName = ErasedName;
                    await store.StoreAsync(siteId, DidinKind.ReviewVerdict, fileNumber, 0, verdict, ct);
                    verdicts++;
                }
            }

            return new[]
            {
                new ErasureOutcome(UserStateStore, ErasureHandling.Delete, states),
                new ErasureOutcome(RatingsStore, ErasureHandling.Delete, ratings.Count),
                new ErasureOutcome(VerdictsStore, ErasureHandling.Anonymise, verdicts),
            };
        }
    }

    /// <summary>
    /// What Didin holds about a person, for their data package: their list, progress, seen marks and subtitle choice, and
    /// their ratings and reviews. Explicit fields, never the stored document.
    /// </summary>
    public sealed class DidinSubjectData(DidinStore store) : ISubjectDataContributor
    {
        public string SubjectDataType => "didin";

        public async Task<IReadOnlyList<PortableEntity>> ExportForSubjectAsync(SubjectScope scope, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(scope);
            var entities = new List<PortableEntity>();
            if (scope.SiteId <= 0 || scope.UserId <= 0) return entities;

            var state = await store.LoadAsync<UserStateDoc>(scope.SiteId, DidinKind.UserState, scope.UserId, ct: cancellationToken);
            if (state != null)
            {
                entities.Add(new PortableEntity(new PortableEntityRef("didin-user-state", scope.UserId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    "Didin: list, progress and seen titles", 1, JsonSerializer.Serialize(new
                    {
                        watchlist = state.Watchlist,
                        progress = state.Progress.Select(p => new { file = p.Key, titleId = p.Value.TitleId, episodeId = p.Value.EpisodeId, position = p.Value.Position, duration = p.Value.Duration, updatedUtc = p.Value.UpdatedUtc }),
                        seen = state.Seen.Select(s => new { titleId = s.TitleId, seenUtc = s.SeenUtc }),
                        finishedNotRated = state.FinishedSkipped,
                        subtitleLanguage = state.SubtitleLang,
                    })));
            }

            foreach (var rating in (await store.AllAsync<RatingDoc>(scope.SiteId, DidinKind.Rating, cancellationToken)).Where(r => r.UserId == scope.UserId).OrderBy(r => r.UpdatedUtc))
            {
                entities.Add(new PortableEntity(new PortableEntityRef("didin-rating", rating.TitleId.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    $"Didin: rating of title {rating.TitleId}", 1, JsonSerializer.Serialize(new
                    {
                        titleId = rating.TitleId,
                        stars = rating.Stars,
                        review = rating.Review,
                        updatedUtc = rating.UpdatedUtc,
                    })));
            }
            return entities;
        }
    }
}
