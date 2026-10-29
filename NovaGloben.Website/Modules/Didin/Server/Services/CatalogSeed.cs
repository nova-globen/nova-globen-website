using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Didin.Server.Documents;
using Didin.Server.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Didin.Server.Services
{
    /// <summary>
    /// A catalogue a host brings with it: <c>Didin:SeedCatalog</c> names a catalog.json, a local file or an https address
    /// (production's sample: https://media.nova-globen.se/catalog.json), in the import's own shape. When the site's
    /// catalogue is EMPTY it is imported through the same checks as the API import, and each title's poster is taken
    /// from posters/&lt;number&gt;.jpg beside it. A catalogue with anything in it is never touched, so the seed runs once
    /// and no import can be undone by a restart. Nothing set, nothing done.
    /// </summary>
    public sealed class CatalogSeed(IConfiguration configuration, DidinStore store, CatalogImport import, ILogger<CatalogSeed> logger)
    {
        public const string Key = "Didin:SeedCatalog";
        private const int MaxCatalogBytes = 4 * 1024 * 1024;
        private const int MaxPosterBytes = 8 * 1024 * 1024;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

        /// <summary>Imports the seed into an empty catalogue; says what it did, or null when it did nothing.</summary>
        public async Task<string> SeedIfEmptyAsync(DidinSite site, CancellationToken ct = default)
        {
            var source = configuration[Key]?.Trim();
            if (string.IsNullOrEmpty(source))
            {
                return null;
            }
            if (await store.LoadAsync<CatalogVersionDoc>(site.SiteId, DidinKind.CatalogVersion, 0, ct: ct) != null
                || (await store.AllAsync<TitleDoc>(site.SiteId, DidinKind.Title, ct)).Count > 0)
            {
                return null;
            }

            CatalogImportRequest request;
            try
            {
                request = JsonSerializer.Deserialize<CatalogImportRequest>(await ReadAsync(source, MaxCatalogBytes, ct), DidinStore.Json);
            }
            catch (Exception ex) when (ex is IOException or HttpRequestException or JsonException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
            {
                logger.LogError("Didin: the seed catalogue {Source} could not be read ({Error}); the catalogue stays empty.", source, ex.GetType().Name);
                return null;
            }
            if (request == null)
            {
                logger.LogError("Didin: the seed catalogue {Source} is empty; the catalogue stays empty.", source);
                return null;
            }

            var result = await import.ImportAsync(site, request, ct);
            if (!result.Ok)
            {
                logger.LogError("Didin: the seed catalogue {Source} was refused, nothing stored: {Problems}", source, string.Join("; ", result.Problems));
                return null;
            }

            var posters = 0;
            foreach (var title in request.Titles)
            {
                var number = CatalogQuery.NumberOf(title.Id);
                var poster = Beside(source, $"posters/{number}.jpg");
                try
                {
                    await import.ImportPosterAsync(site, number, await ReadAsync(poster, MaxPosterBytes, ct), "image/jpeg", ct);
                    posters++;
                }
                catch (Exception ex) when (ex is IOException or HttpRequestException or InvalidDataException or TaskCanceledException or UnauthorizedAccessException)
                {
                    logger.LogWarning("Didin: no poster for title {Number} at {Poster} ({Error}).", number, poster, ex.GetType().Name);
                }
            }

            var said = $"seeded {result.Titles} titles, {result.Files} files and {posters} posters from {source}";
            logger.LogInformation("Didin: {Said}.", said);
            return said;
        }

        /// <summary>An address beside the catalogue's: the same folder, local or https.</summary>
        internal static string Beside(string source, string relative)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                return new Uri(uri, relative).AbsoluteUri;
            }
            return Path.Combine(Path.GetDirectoryName(Path.GetFullPath(source)) ?? "", relative.Replace('/', Path.DirectorySeparatorChar));
        }

        /// <summary>A local file, or an https address (plain http is refused: a seed is trusted content), up to a size.</summary>
        internal static async Task<byte[]> ReadAsync(string source, int maxBytes, CancellationToken ct)
        {
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.Scheme != Uri.UriSchemeFile)
            {
                if (uri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidDataException("only https or a local file");
                }
                using var response = await Http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                if (response.Content.Headers.ContentLength > maxBytes)
                {
                    throw new InvalidDataException("too large");
                }
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                return await ReadCappedAsync(stream, maxBytes, ct);
            }
            await using var file = File.OpenRead(source);
            return await ReadCappedAsync(file, maxBytes, ct);
        }

        private static async Task<byte[]> ReadCappedAsync(Stream stream, int maxBytes, CancellationToken ct)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                {
                    throw new InvalidDataException("too large");
                }
                buffer.Write(chunk, 0, read);
            }
            return buffer.ToArray();
        }
    }
}
