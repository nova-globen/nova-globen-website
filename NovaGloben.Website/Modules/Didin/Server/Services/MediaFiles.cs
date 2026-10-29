using System;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Didin.Server.Services
{
    /// <summary>
    /// Reads a catalogue path from the host's <see cref="MediaSource"/>. READ ONLY BY CONTRACT: the WebDAV store has no
    /// read-only users, so its user can write; this issues GET and nothing else - never PUT, DELETE, MOVE or MKCOL.
    /// </summary>
    public sealed class MediaFiles(MediaSource source, CatalogQuery query)
    {
        private static readonly HttpClient Http = new(new HttpClientHandler
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        });

        private static readonly Regex Range = new(@"^bytes=(\d*)-(\d*)$", RegexOptions.Compiled);

        public MediaSource Source => source;

        /// <summary>A GET of the path on the WebDAV store, headers read, with the caller's Range if any; null without a store.</summary>
        public async Task<HttpResponseMessage> GetWebDavAsync(string filePath, string range, CancellationToken ct)
        {
            var url = query.WebDavUrl(filePath);
            if (!source.IsWebDav || url == null) return null;
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = source.WebDavAuthorization;
            if (!string.IsNullOrEmpty(range))
            {
                request.Headers.TryAddWithoutValidation("Range", range);
            }
            return await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        }

        /// <summary>A small file (a sidecar subtitle) whole, or null when it is missing, outside the root or over the limit.</summary>
        public async Task<byte[]> ReadSmallAsync(string filePath, long maxBytes, CancellationToken ct)
        {
            if (source.IsLocal)
            {
                var full = source.LocalPath(filePath);
                if (full == null || !File.Exists(full) || new FileInfo(full).Length > maxBytes) return null;
                return await File.ReadAllBytesAsync(full, ct);
            }
            using var response = await GetWebDavAsync(filePath, null, ct);
            if (response == null || !response.IsSuccessStatusCode || response.Content.Headers.ContentLength > maxBytes)
            {
                return null;
            }
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            return bytes.LongLength > maxBytes ? null : bytes;
        }

        /// <summary>The type a player is told, from the extension: a store may call an .mp4 a generic file, which Safari refuses.</summary>
        public static string ContentType(string fileName) => Path.GetExtension(fileName)?.ToLowerInvariant() switch
        {
            ".mp4" or ".m4v" => "video/mp4",
            ".mkv" => "video/x-matroska",
            ".avi" => "video/x-msvideo",
            ".vob" or ".mpg" or ".mpeg" or ".m2v" => "video/mpeg",
            ".mov" => "video/quicktime",
            ".wmv" => "video/x-ms-wmv",
            ".webm" => "video/webm",
            ".ts" or ".m2ts" => "video/mp2t",
            ".flv" => "video/x-flv",
            _ => "application/octet-stream",
        };

        /// <summary>A single byte range of a file of the given length: bytes=0-1023 | bytes=1024- | bytes=-500.</summary>
        public static bool TryParseRange(string header, long length, out long start, out long end)
        {
            start = 0;
            end = length - 1;
            var m = Range.Match(header ?? "");
            if (!m.Success || length <= 0) return false;
            var first = m.Groups[1].Value;
            var last = m.Groups[2].Value;
            if (first == "")
            {
                if (!long.TryParse(last, out var suffix) || suffix == 0) return false;
                start = Math.Max(0, length - suffix);
            }
            else
            {
                if (!long.TryParse(first, out start)) return false;
                if (last != "")
                {
                    if (!long.TryParse(last, out var lastByte)) return false;
                    end = Math.Min(lastByte, length - 1);
                }
            }
            return start < length && start <= end;
        }
    }
}
