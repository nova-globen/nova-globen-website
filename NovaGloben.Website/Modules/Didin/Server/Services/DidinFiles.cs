using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Didin.Server.Services
{
    /// <summary>
    /// Didin's binary files - cached posters, extracted subtitle tracks - in a folder of the data volume, each named by
    /// the hash of its content, so a file is written once and its name doubles as its cache key in URLs. The documents
    /// hold the hash. The folder rides the nightly copy of the data volume; its content can be re-derived from the media.
    /// </summary>
    public sealed class DidinFiles
    {
        /// <summary>The setting naming the folder; relative to the data folder unless absolute.</summary>
        public const string PathKey = "Didin:CachePath";

        private readonly string _root;

        public DidinFiles(IConfiguration configuration, IHostEnvironment environment)
            : this(Resolve(configuration[PathKey], DataFolder(environment)))
        {
        }

        /// <summary>A cache in <paramref name="root"/>, for tests.</summary>
        public DidinFiles(string root)
        {
            _root = root;
        }

        /// <summary>Stores the bytes and returns their hash, the name they are kept under.</summary>
        public string Save(byte[] content, string kind)
        {
            ArgumentNullException.ThrowIfNull(content);
            var hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant()[..32];
            var path = PathOf(kind, hash);
            if (!File.Exists(path))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var temporary = path + ".tmp";
                File.WriteAllBytes(temporary, content);
                File.Move(temporary, path, overwrite: true);
            }
            return hash;
        }

        /// <summary>The stored bytes' stream, or null when there are none under that hash.</summary>
        public Stream Open(string kind, string hash)
        {
            if (string.IsNullOrEmpty(hash) || hash.Length != 32 || !IsHex(hash))
            {
                return null;
            }
            var path = PathOf(kind, hash);
            return File.Exists(path) ? File.OpenRead(path) : null;
        }

        private string PathOf(string kind, string hash) => Path.Combine(_root, kind, hash[..2], hash);

        private static bool IsHex(string value)
        {
            foreach (var c in value)
            {
                if (!Uri.IsHexDigit(c)) return false;
            }
            return true;
        }

        private static string DataFolder(IHostEnvironment environment) =>
            Environment.GetEnvironmentVariable(Stomme.Shared.Constants.AppSettingsOverrideEnvironmentVariable) is { Length: > 0 } data
                ? data
                : Path.Combine(environment.ContentRootPath, "Data");

        private static string Resolve(string configured, string dataFolder) =>
            string.IsNullOrWhiteSpace(configured) ? Path.Combine(dataFolder, "didin-cache")
            : Path.IsPathRooted(configured) ? configured
            : Path.Combine(dataFolder, configured);
    }
}
