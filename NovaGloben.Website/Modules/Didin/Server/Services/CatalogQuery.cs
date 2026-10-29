using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Storage;
using Didin.Shared.Models;
using Microsoft.Extensions.Configuration;

namespace Didin.Server.Services
{
    /// <summary>
    /// A site's whole catalogue at one version, read from the module store once and answered from memory: the grid, a
    /// title's page, a person's titles, "more like this" and a file's details. It is a pure function of the stored
    /// documents, and every write path (an import, a poster) moves the version, so it is rebuilt only when that changes.
    /// </summary>
    public sealed class CatalogSnapshot
    {
        // Who made each title, for "More like this".
        internal sealed record Profile(int Id, string Title, int? Year, double? Rating, HashSet<string> Directors,
                                       HashSet<string> Cast, HashSet<string> Genres, HashSet<string> Collections);

        internal string Version { get; init; }
        internal DateTime GeneratedUtc { get; init; }
        internal int TitleCount { get; init; }
        internal Dictionary<int, TitleDoc> Titles { get; init; }
        internal Dictionary<int, FileDoc> Files { get; init; }
        internal ILookup<int, FileDoc> ListedFilesByTitle { get; init; }
        internal ILookup<int, EpisodeDoc> EpisodesByTitle { get; init; }
        internal Dictionary<int, EpisodeDoc> EpisodesById { get; init; }
        internal List<CollectionDoc> Collections { get; init; }
        internal Dictionary<int, string> Posters { get; init; }
        internal HashSet<int> WidePosters { get; init; } = new();
        internal CatalogDocument Catalog { get; init; }
        internal Dictionary<string, Dictionary<int, PersonTitle>> People { get; init; }
        internal List<Profile> Profiles { get; init; }
    }

    /// <summary>The snapshots of every Didin site on this host, by site.</summary>
    public sealed class CatalogCache
    {
        private readonly ConcurrentDictionary<int, CatalogSnapshot> _bySite = new();

        internal bool TryGet(int siteId, string version, out CatalogSnapshot snapshot) =>
            _bySite.TryGetValue(siteId, out snapshot) && snapshot.Version == version;

        internal void Set(int siteId, CatalogSnapshot snapshot) => _bySite[siteId] = snapshot;
    }

    /// <summary>Reads a site's catalogue from the module store and projects the API's payloads.</summary>
    public sealed class CatalogQuery
    {
        private readonly DidinStore _store;
        private readonly CatalogCache _cache;
        private readonly string _webDavBase;
        private readonly bool _includeDirectUrls;

        public CatalogQuery(DidinStore store, CatalogCache cache, IConfiguration configuration)
        {
            _store = store;
            _cache = cache;
            // The media store's address is the deployment's; there is no default (a host without one serves no WebDAV).
            _webDavBase = configuration["MediaStore:WebDavBase"]?.Trim().TrimEnd('/');
            _includeDirectUrls = string.Equals(configuration["MediaStore:DirectUrls"], "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The WebDAV address of a share path, or null when the host names no WebDAV store.</summary>
        public string WebDavUrl(string filePath) =>
            string.IsNullOrEmpty(_webDavBase) || filePath is null
                ? null
                : _webDavBase + "/" + string.Join('/', filePath.Split('/').Select(Uri.EscapeDataString));

        /// <summary>
        /// The poster address a client may load: the cached copy (<c>{site}/api/art/{id}?v={hash}</c>) or none - a title
        /// without a cached poster shows its initials rather than reaching out to another site at render time.
        /// </summary>
        private static string ArtUrl(DidinSite site, int titleId, IReadOnlyDictionary<int, string> posters) =>
            posters.TryGetValue(titleId, out var hash) ? site.Url($"/api/art/{titleId}?v={hash}") : null;

        /// <summary>The catalogue's number in a document id: 123 in "titles/123".</summary>
        internal static int NumberOf(string id) =>
            id != null && id.IndexOf('/') is var slash and > 0 && int.TryParse(id[(slash + 1)..], out var n) ? n : 0;

        /// <summary>
        /// The catalogue's version alone - every page load asks for it to validate its cached catalogue, so it reads one
        /// small document instead of building the whole catalogue.
        /// </summary>
        public async Task<(string Version, int TitleCount, DateTime GeneratedUtc)> GetVersionAsync(DidinSite site, CancellationToken ct = default)
        {
            var version = await _store.LoadAsync<CatalogVersionDoc>(site.SiteId, DidinKind.CatalogVersion, 0, ct: ct);
            return (version?.Version ?? "0", version?.TitleCount ?? 0, version?.SeededUtc ?? DateTime.UtcNow);
        }

        /// <summary>
        /// A file the site offers. One gone from the share keeps its title in the catalogue but is "missing" until it
        /// returns; one an administrator set aside is "ignored". Neither is listed.
        /// </summary>
        private static bool Listed(FileDoc f) => f.Status is not ("missing" or "ignored");

        /// <summary>
        /// The catalogue's credits: every licensor of a listed title that names one, with the licences their titles are
        /// under, in order. Built from the titles, so the line names exactly what the site shows and nothing else.
        /// </summary>
        internal static List<CatalogCredit> Credits(IEnumerable<TitleDoc> listed) =>
            listed.Where(t => !string.IsNullOrWhiteSpace(t.Licensor))
                .GroupBy(t => t.Licensor.Trim(), StringComparer.OrdinalIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                .Select(g => new CatalogCredit
                {
                    Licensor = g.Key,
                    LicensorUrl = g.Select(t => t.LicensorUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)),
                    Licenses = g.Where(t => !string.IsNullOrWhiteSpace(t.License))
                        .GroupBy(t => t.License.Trim(), StringComparer.OrdinalIgnoreCase)
                        .OrderBy(l => l.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(l => new CatalogLicense { Name = l.Key, Url = l.Select(t => t.LicenseUrl).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) })
                        .ToList(),
                })
                .ToList();

        /// <summary>
        /// Which of a series' own episodes a file of it plays. Its <c>EpisodeId</c>, when that is one of the series'
        /// episodes. The old catalogue also holds files whose <c>EpisodeId</c> still names the same-numbered episode of
        /// another series (a series matched once to the wrong show and then corrected): the file's own title is the
        /// authority, so such a file plays the series' own episode with that season and number. Null when neither
        /// finds one: the file is then not offered on an episode.
        /// </summary>
        internal static Func<FileDoc, int?> EpisodeResolver(CatalogSnapshot snapshot, List<EpisodeDoc> ownEpisodes)
        {
            var own = ownEpisodes.Select(e => NumberOf(e.Id)).ToHashSet();
            var bySlot = ownEpisodes.GroupBy(e => (e.Season, e.Episode)).ToDictionary(g => g.Key, g => NumberOf(g.First().Id));
            return file =>
            {
                if (file.EpisodeId is not int number) return null;
                if (own.Contains(number)) return number;
                return snapshot.EpisodesById.TryGetValue(number, out var named)
                    && bySlot.TryGetValue((named.Season, named.Episode), out var mine) ? mine : null;
            };
        }

        public async Task<CatalogDocument> GetCatalogAsync(DidinSite site, CancellationToken ct = default) =>
            (await SnapshotAsync(site, ct)).Catalog;

        /// <summary>A person's titles, or null for a name the catalogue does not credit.</summary>
        public async Task<PersonTitles> GetPersonAsync(DidinSite site, string name, CancellationToken ct = default)
        {
            var snapshot = await SnapshotAsync(site, ct);
            if (string.IsNullOrWhiteSpace(name) || !snapshot.People.TryGetValue(name.Trim(), out var byTitle)) return null;
            var titles = byTitle.Values.ToList();
            var order = new[] { "director", "creator", "actor", "writer", "producer", "composer" };
            return new PersonTitles
            {
                Name = name.Trim(),
                Roles = titles.SelectMany(t => t.Roles).Distinct()
                    .OrderBy(r => Array.IndexOf(order, r) is var i && i < 0 ? 99 : i).ToList(),
                Titles = titles,
            };
        }

        internal async Task<CatalogSnapshot> SnapshotAsync(DidinSite site, CancellationToken ct)
        {
            var version = await _store.LoadAsync<CatalogVersionDoc>(site.SiteId, DidinKind.CatalogVersion, 0, ct: ct);
            var key = version?.Version ?? "0";
            if (_cache.TryGet(site.SiteId, key, out var cached))
            {
                return cached;
            }
            var snapshot = await BuildAsync(site, version, ct);
            _cache.Set(site.SiteId, snapshot);
            return snapshot;
        }

        private async Task<CatalogSnapshot> BuildAsync(DidinSite site, CatalogVersionDoc version, CancellationToken ct)
        {
            var posterDocs = (await _store.AllAsync<PosterDoc>(site.SiteId, DidinKind.Poster, ct))
                .GroupBy(p => p.TitleId).ToDictionary(g => g.Key, g => g.Last());
            var posters = posterDocs.ToDictionary(kv => kv.Key, kv => kv.Value.Hash);
            var widePosters = posterDocs.Where(kv => ImageSize.IsWide(kv.Value.Width, kv.Value.Height)).Select(kv => kv.Key).ToHashSet();
            var titles = (await _store.AllAsync<TitleDoc>(site.SiteId, DidinKind.Title, ct))
                .GroupBy(t => NumberOf(t.Id)).ToDictionary(g => g.Key, g => g.Last());
            var files = (await _store.AllAsync<FileDoc>(site.SiteId, DidinKind.File, ct))
                .GroupBy(f => NumberOf(f.Id)).ToDictionary(g => g.Key, g => g.Last());
            var episodes = await _store.AllAsync<EpisodeDoc>(site.SiteId, DidinKind.Episode, ct);
            var collections = await _store.AllAsync<CollectionDoc>(site.SiteId, DidinKind.Collection, ct);

            var filesByTitle = files.Values.Where(f => f.TitleId != null && Listed(f)).ToLookup(f => f.TitleId!.Value);
            var epsByTitle = episodes.ToLookup(e => e.TitleId);
            var orderByTitle = collections
                .SelectMany(c => c.Members.Select(m => (m.TitleId, c.Name, m.Order)))
                .GroupBy(x => x.TitleId)
                .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Order ?? double.MaxValue).First());

            var doc = new CatalogDocument
            {
                Version = version?.Version ?? "0",
                GeneratedUtc = version?.SeededUtc ?? DateTime.UtcNow,
                TitleCount = titles.Count,
            };

            foreach (var (tid, t) in titles)
            {
                var tfiles = filesByTitle[tid].ToList();
                // a title with nothing to play (its only file damaged and set aside) is not listed
                if (tfiles.Count == 0) continue;
                orderByTitle.TryGetValue(tid, out var coll);
                doc.Cards.Add(new CatalogCard
                {
                    Id = tid,
                    Kind = t.Kind,
                    Title = t.Title,
                    OriginalTitle = t.OriginalTitle,
                    Year = t.Year,
                    EndYear = t.EndYear,
                    PosterUrl = ArtUrl(site, tid, posters),
                    PosterWide = widePosters.Contains(tid),
                    Genres = t.Genres,
                    RuntimeMinutes = t.RuntimeMinutes,
                    ContentRating = t.ContentRating,
                    ImdbRating = t.ImdbRating,
                    RottenTomatoes = t.RottenTomatoes,
                    Quality = BestQuality(tfiles),
                    Languages = t.Languages,
                    Collection = coll.Name,
                    CollectionOrder = coll.Order.HasValue ? (int?)coll.Order : null,
                    EpisodeCount = epsByTitle[tid].Count(),
                    FileCount = tfiles.Count,
                });
            }

            doc.Facets = new CatalogFacets
            {
                Genres = doc.Cards.SelectMany(c => c.Genres).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                Types = doc.Cards.Select(c => c.Kind).Distinct().OrderBy(x => x).ToList(),
                Qualities = doc.Cards.Select(c => c.Quality).Where(x => x != null).Distinct().OrderBy(x => x).ToList(),
                Languages = doc.Cards.SelectMany(c => c.Languages).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                Collections = doc.Cards.Select(c => c.Collection).Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                Ratings = doc.Cards.Select(c => c.ContentRating).Where(x => x != null).Distinct().OrderBy(x => x).ToList(),
                MinYear = doc.Cards.Where(c => c.Year != null).Select(c => c.Year).Min(),
                MaxYear = doc.Cards.Where(c => c.Year != null).Select(c => c.Year).Max(),
                MinRuntime = doc.Cards.Where(c => c.RuntimeMinutes != null).Select(c => c.RuntimeMinutes).Min(),
                MaxRuntime = doc.Cards.Where(c => c.RuntimeMinutes != null).Select(c => c.RuntimeMinutes).Max(),
            };

            var listedIds = doc.Cards.Select(c => c.Id).ToHashSet();
            doc.Credits = Credits(titles.Where(kv => listedIds.Contains(kv.Key)).Select(kv => kv.Value));

            // who is in what: built with the catalogue, so it always answers for the titles the site lists
            var listed = doc.Cards.Select(c => c.Id).ToHashSet();
            var people = new Dictionary<string, Dictionary<int, PersonTitle>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (id, t) in titles)
            {
                if (!listed.Contains(id)) continue;
                foreach (var p in t.People.Where(p => !string.IsNullOrWhiteSpace(p.Name)))
                {
                    var name = p.Name.Trim();
                    if (!people.TryGetValue(name, out var byTitle)) people[name] = byTitle = new Dictionary<int, PersonTitle>();
                    if (!byTitle.TryGetValue(id, out var entry)) byTitle[id] = entry = new PersonTitle { TitleId = id };
                    if (p.Role != null && !entry.Roles.Contains(p.Role)) entry.Roles.Add(p.Role);
                    entry.Character ??= p.Character;
                }
            }

            var profiles = titles.Select(kv => new CatalogSnapshot.Profile(
                kv.Key, kv.Value.Title, kv.Value.Year, kv.Value.ImdbRating,
                kv.Value.People.Where(p => p.Role == "director").Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase),
                kv.Value.People.Where(p => p.Role == "actor").OrderBy(p => p.Billing ?? 999).Take(6).Select(p => p.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase),
                kv.Value.Genres.ToHashSet(StringComparer.OrdinalIgnoreCase),
                kv.Value.Collections.ToHashSet(StringComparer.OrdinalIgnoreCase))).ToList();

            return new CatalogSnapshot
            {
                Version = doc.Version,
                GeneratedUtc = doc.GeneratedUtc,
                TitleCount = titles.Count,
                Titles = titles,
                Files = files,
                ListedFilesByTitle = filesByTitle,
                EpisodesByTitle = epsByTitle,
                EpisodesById = episodes.GroupBy(e => NumberOf(e.Id)).ToDictionary(g => g.Key, g => g.Last()),
                Collections = collections,
                Posters = posters,
                WidePosters = widePosters,
                Catalog = doc,
                People = people,
                Profiles = profiles,
            };
        }

        public async Task<TitleDetails> GetDetailsAsync(DidinSite site, int id, CancellationToken ct = default)
        {
            var snapshot = await SnapshotAsync(site, ct);
            if (!snapshot.Titles.TryGetValue(id, out var t)) return null;

            var files = snapshot.ListedFilesByTitle[id]
                .OrderBy(f => f.EpisodeId ?? 0).ThenBy(f => f.Part ?? 1)
                .ToList();
            var episodes = snapshot.EpisodesByTitle[id].OrderBy(e => e.Season).ThenBy(e => e.Episode).ToList();
            var collections = snapshot.Collections.Where(c => c.Members.Any(m => m.TitleId == id)).ToList();

            var details = new TitleDetails
            {
                Id = id,
                Kind = t.Kind,
                Title = t.Title,
                OriginalTitle = t.OriginalTitle,
                Year = t.Year,
                EndYear = t.EndYear,
                Tagline = t.Tagline,
                Plot = t.Plot,
                PlotFull = t.PlotFull,
                RuntimeMinutes = t.RuntimeMinutes,
                ContentRating = t.ContentRating,
                Genres = t.Genres,
                Keywords = t.Keywords,
                Languages = t.Languages,
                Countries = t.Countries,
                ImdbRating = t.ImdbRating,
                RottenTomatoes = t.RottenTomatoes,
                Metascore = t.Metascore,
                TrailerUrl = t.TrailerUrl,
                Awards = t.Awards,
                Licensor = t.Licensor,
                LicensorUrl = t.LicensorUrl,
                Attribution = t.Attribution,
                License = t.License,
                LicenseUrl = t.LicenseUrl,
                SourceUrl = t.SourceUrl,
                BoxOffice = t.BoxOffice,
                Budget = t.Budget,
                Network = t.Network,
                WikipediaUrl = t.WikipediaUrl,
                ImdbUrl = t.ImdbUrl,
                People = t.People,
            };

            if (t.Kind == "series")
            {
                var episodeOf = EpisodeResolver(snapshot, episodes);
                var epsByFile = files.Select(f => (File: f, Episode: episodeOf(f))).Where(x => x.Episode != null)
                    .ToLookup(x => x.Episode!.Value, x => x.File);
                foreach (var group in episodes.GroupBy(e => e.Season).OrderBy(g => g.Key))
                {
                    var season = new SeasonInfo { Season = group.Key };
                    foreach (var e in group.OrderBy(x => x.Episode))
                    {
                        var episodeId = NumberOf(e.Id);
                        season.Episodes.Add(new EpisodeInfo
                        {
                            Id = episodeId,
                            Season = e.Season,
                            Number = e.Episode,
                            NumberEnd = e.EpisodeEnd,
                            Name = e.Name,
                            AirDate = e.AirDate,
                            Plot = e.Plot,
                            RuntimeMinutes = e.RuntimeMinutes,
                            Rating = e.Rating,
                            Files = epsByFile[episodeId].Select(f => DescribeFile(site, f)).ToList(),
                        });
                    }
                    details.Seasons.Add(season);
                }
            }
            else
            {
                details.Files = files.Select(f => DescribeFile(site, f)).ToList();
            }

            // the franchise row - only titles the grid lists: a title with no file left is not offered from another's page
            var listed = snapshot.Catalog.Cards.Select(c => c.Id).ToHashSet();
            var related = collections
                .SelectMany(c => c.Members.Select(m => (c.Name, m)))
                .OrderBy(x => x.m.Order ?? double.MaxValue)
                .Where(x => x.m.TitleId != id)
                .Take(20)
                .Where(x => listed.Contains(x.m.TitleId))
                .ToList();
            details.Recommended = Recommend(snapshot, id).Where(r => listed.Contains(r.Id)).ToList();
            details.PosterUrl = ArtUrl(site, id, snapshot.Posters);
            details.PosterWide = snapshot.WidePosters.Contains(id);
            foreach (var r in details.Recommended)
            {
                r.PosterUrl = ArtUrl(site, r.Id, snapshot.Posters);
                r.PosterWide = snapshot.WidePosters.Contains(r.Id);
            }
            foreach (var (name, m) in related)
            {
                if (!snapshot.Titles.TryGetValue(m.TitleId, out var rt)) continue;
                details.Related.Add(new RelatedTitle
                {
                    Id = m.TitleId,
                    Title = rt.Title,
                    Year = rt.Year,
                    PosterUrl = ArtUrl(site, m.TitleId, snapshot.Posters),
                    PosterWide = snapshot.WidePosters.Contains(m.TitleId),
                    Collection = name,
                    CollectionOrder = m.Order,
                });
            }
            return details;
        }

        /// <summary>
        /// Titles like this one: a shared director weighs 3, each shared top-billed actor 2, each shared genre 0.5. A pick
        /// needs a shared person or three shared genres. Titles of the same franchise are left to the franchise row.
        /// </summary>
        private static List<RelatedTitle> Recommend(CatalogSnapshot snapshot, int id, int take = 12)
        {
            var me = snapshot.Profiles.FirstOrDefault(p => p.Id == id);
            if (me == null) return new();
            return snapshot.Profiles
                .Where(p => p.Id != id && !p.Collections.Overlaps(me.Collections))
                .Select(p =>
                {
                    var directors = p.Directors.Intersect(me.Directors, StringComparer.OrdinalIgnoreCase).ToList();
                    var cast = p.Cast.Intersect(me.Cast, StringComparer.OrdinalIgnoreCase).ToList();
                    var genres = p.Genres.Count(g => me.Genres.Contains(g));
                    var score = directors.Count * 3 + cast.Count * 2 + genres * 0.5;
                    var reason = directors.Count > 0 ? $"Directed by {directors[0]}"
                               : cast.Count > 0 ? $"With {string.Join(", ", cast.Take(2))}"
                               : "Similar genres";
                    return (p, score, reason, people: directors.Count + cast.Count, genres);
                })
                .Where(x => x.people > 0 || x.genres >= 3)
                .OrderByDescending(x => x.score).ThenByDescending(x => x.p.Rating ?? 0)
                .Take(take)
                .Select(x => new RelatedTitle { Id = x.p.Id, Title = x.p.Title, Year = x.p.Year, Reason = x.reason })
                .ToList();
        }

        /// <summary>A file of the catalogue by its document id ("files/123"), or null.</summary>
        public async Task<FileDoc> GetFileAsync(DidinSite site, string id, CancellationToken ct = default)
        {
            var number = NumberOf(id);
            if (number == 0 || !id.StartsWith("files/", StringComparison.Ordinal)) return null;
            var snapshot = await SnapshotAsync(site, ct);
            return snapshot.Files.TryGetValue(number, out var file) ? file : null;
        }

        /// <summary>The player's view of a file - used by the continue-watching list too.</summary>
        public MediaFileInfo DescribeFile(DidinSite site, FileDoc f) => new()
        {
            Token = FileToken.Encode(f.Id),
            FileName = f.FileName,
            Quality = f.Quality,
            Source = f.Source,
            Codec = f.Codec,
            Audio = f.Audio,
            Edition = f.Edition,
            Languages = f.Languages,
            SizeBytes = f.SizeBytes,
            Part = f.Part,
            PartCount = f.PartCount,
            WebDavUrl = _includeDirectUrls ? WebDavUrl(f.PlaybackPath ?? f.FilePath) : null,
            Playback = f.Playback,
            Role = f.Role,
            DurationSeconds = f.DurationSeconds,
            Codecs = f.Codecs,
            Subtitles = SubtitleLabels.Describe(f.FilePath, f.Subtitles, i => site.Url($"/api/subs/{FileToken.Encode(f.Id)}/{i}")),
        };

        private static string BestQuality(List<FileDoc> files)
        {
            var order = new[] { "2160p", "1080p", "720p", "576p", "480p", "360p", "SD" };
            return files.Where(f => f.Quality != null)
                .OrderBy(f => Array.IndexOf(order, f.Quality) is int i and >= 0 ? i : 99)
                .Select(f => f.Quality)
                .FirstOrDefault();
        }
    }
}
