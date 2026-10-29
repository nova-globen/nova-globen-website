using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Storage;

namespace Didin.Server.Services
{
    /// <summary>A catalogue to import: its documents, each with its own id ("titles/123"), and a label for the version.</summary>
    public sealed class CatalogImportRequest
    {
        public string Version { get; set; }
        public List<TitleDoc> Titles { get; set; } = new();
        public List<EpisodeDoc> Episodes { get; set; } = new();
        public List<FileDoc> Files { get; set; } = new();
        public List<CollectionDoc> Collections { get; set; } = new();
    }

    /// <summary>What an import wrote, or why it refused.</summary>
    public sealed record CatalogImportResult(bool Ok, string Version, int Titles, int Episodes, int Files, int Collections, IReadOnlyList<string> Problems);

    /// <summary>
    /// Brings a site's catalogue in from outside: the documents, upserted by their numbers (an import never deletes; a
    /// file that is gone is marked "missing", one set aside "ignored"), then a new catalogue version, which every
    /// client's cached copy and the server's snapshot are checked against. Posters and extracted subtitle tracks come one
    /// at a time: their bytes into the module's file cache, their metadata into documents. Nothing about the media itself
    /// is in this code; whoever runs an import brings the catalogue.
    /// </summary>
    public sealed class CatalogImport(DidinStore store, DidinFiles files, TimeProvider clock)
    {
        public const string PostersFolder = "posters";
        public const string SubtitlesFolder = "subtitles";

        public async Task<CatalogImportResult> ImportAsync(DidinSite site, CatalogImportRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            var problems = new List<string>();
            Check(request.Titles, "titles", t => t.Id, problems);
            Check(request.Episodes, "episodes", e => e.Id, problems);
            Check(request.Files, "files", f => f.Id, problems);
            Check(request.Collections, "collections", c => c.Id, problems);
            if (problems.Count > 0)
            {
                return new CatalogImportResult(false, null, 0, 0, 0, 0, problems);
            }

            foreach (var t in request.Titles) await store.StoreAsync(site.SiteId, DidinKind.Title, CatalogQuery.NumberOf(t.Id), 0, t, ct);
            foreach (var e in request.Episodes) await store.StoreAsync(site.SiteId, DidinKind.Episode, CatalogQuery.NumberOf(e.Id), 0, e, ct);
            foreach (var f in request.Files) await store.StoreAsync(site.SiteId, DidinKind.File, CatalogQuery.NumberOf(f.Id), 0, f, ct);
            foreach (var c in request.Collections) await store.StoreAsync(site.SiteId, DidinKind.Collection, CatalogQuery.NumberOf(c.Id), 0, c, ct);

            var titles = (await store.AllAsync<TitleDoc>(site.SiteId, DidinKind.Title, ct)).Count;
            var allFiles = (await store.AllAsync<FileDoc>(site.SiteId, DidinKind.File, ct)).Count;
            var version = await BumpAsync(site, request.Version, titles, allFiles, ct);
            return new CatalogImportResult(true, version, request.Titles.Count, request.Episodes.Count, request.Files.Count, request.Collections.Count, problems);
        }

        /// <summary>A title's poster image. Moves the catalogue's version: the grid shows posters.</summary>
        public async Task<string> ImportPosterAsync(DidinSite site, int titleId, byte[] image, string contentType, CancellationToken ct = default)
        {
            if (titleId <= 0) throw new ArgumentOutOfRangeException(nameof(titleId));
            ArgumentNullException.ThrowIfNull(image);
            var hash = files.Save(image, PostersFolder);
            var size = ImageSize.Read(image);
            await store.StoreAsync(site.SiteId, DidinKind.Poster, titleId, 0, new PosterDoc
            {
                Id = $"posters/{titleId}",
                TitleId = titleId,
                ContentType = string.IsNullOrWhiteSpace(contentType) ? "image/jpeg" : contentType,
                Length = image.LongLength,
                Hash = hash,
                FetchedUtc = clock.GetUtcNow().UtcDateTime,
                Width = size?.Width,
                Height = size?.Height,
            }, ct);
            var current = await store.LoadAsync<CatalogVersionDoc>(site.SiteId, DidinKind.CatalogVersion, 0, ct: ct);
            await BumpAsync(site, "posters", current?.TitleCount ?? 0, current?.FileCount ?? 0, ct);
            return hash;
        }

        /// <summary>A subtitle track extracted from inside a file, as WebVTT.</summary>
        public async Task<string> ImportSubtitleAsync(DidinSite site, int fileNumber, int stream, string lang, byte[] vtt, CancellationToken ct = default)
        {
            if (fileNumber <= 0) throw new ArgumentOutOfRangeException(nameof(fileNumber));
            if (stream < 0) throw new ArgumentOutOfRangeException(nameof(stream));
            ArgumentNullException.ThrowIfNull(vtt);
            var hash = files.Save(vtt, SubtitlesFolder);
            await store.StoreAsync(site.SiteId, DidinKind.EmbeddedSubtitle, fileNumber, stream, new EmbeddedSubtitleDoc
            {
                Id = $"embeddedsubs/{fileNumber}-{stream}",
                FileId = $"files/{fileNumber}",
                Stream = stream,
                Lang = string.IsNullOrWhiteSpace(lang) ? "und" : lang.Trim(),
                Hash = hash,
            }, ct);
            return hash;
        }

        private async Task<string> BumpAsync(DidinSite site, string label, int titles, int fileCount, CancellationToken ct)
        {
            var now = clock.GetUtcNow().UtcDateTime;
            var stamp = now.ToString("yyyyMMddHHmmssfffffff", System.Globalization.CultureInfo.InvariantCulture);
            var version = string.IsNullOrWhiteSpace(label) ? stamp : $"{Clean(label)}-{stamp}";
            await store.StoreAsync(site.SiteId, DidinKind.CatalogVersion, 0, 0, new CatalogVersionDoc
            {
                Version = version,
                SeededUtc = now,
                TitleCount = titles,
                FileCount = fileCount,
            }, ct);
            return version;
        }

        /// <summary>Every document needs its kind's id with a positive number, once.</summary>
        private static void Check<T>(List<T> documents, string prefix, Func<T, string> id, List<string> problems)
        {
            if (documents == null) return;
            var seen = new HashSet<int>();
            foreach (var document in documents)
            {
                var value = document is null ? null : id(document);
                var number = CatalogQuery.NumberOf(value);
                if (value is null || !value.StartsWith(prefix + "/", StringComparison.Ordinal) || number <= 0)
                {
                    problems.Add($"{prefix}: an id must be \"{prefix}/<positive number>\", not \"{value}\".");
                    continue;
                }
                if (!seen.Add(number))
                {
                    problems.Add($"{prefix}: \"{value}\" is listed twice.");
                }
            }
        }

        private static string Clean(string label) =>
            new string(label.Trim().Where(c => char.IsLetterOrDigit(c) || c is '.' or '-' or '_').Take(40).ToArray());
    }
}
