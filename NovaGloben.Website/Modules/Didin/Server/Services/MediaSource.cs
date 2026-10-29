using System;
using System.IO;
using System.Linq;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace Didin.Server.Services
{
    /// <summary>
    /// Where the media files are, per host, with no default for any of it:
    ///   MediaStore:Root               - a local folder; every path is resolved under it and refused if it leaves it;
    ///   MediaStore:PublicBase         - otherwise a public https store (production's sample, in object storage on its own
    ///                                   host name): the player is sent there, and no byte passes through the site;
    ///   MediaStore:WebDavBase         - otherwise a WebDAV store, read with Basic auth from
    ///   MediaStore:WebDavUser and MediaStore:WebDavPasswordFile (a mounted secret file, read once and trimmed of the
    ///                                   trailing newline every published file carries; a password is never plain config).
    /// With neither, the host has no media and streaming answers 503. Read once per process: a re-published secret
    /// reaches the app when the container is recreated.
    /// </summary>
    public sealed class MediaSource
    {
        private readonly string _root;

        public MediaSource(IConfiguration configuration)
        {
            var root = configuration["MediaStore:Root"];
            if (!string.IsNullOrWhiteSpace(root))
            {
                _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
                return;
            }

            var publicBase = configuration["MediaStore:PublicBase"];
            if (!string.IsNullOrWhiteSpace(publicBase))
            {
                // https only: the address is handed to every visitor's browser, and a plain-http one would be refused
                // as mixed content on an https page anyway
                if (!Uri.TryCreate(publicBase.Trim(), UriKind.Absolute, out var baseUri) || baseUri.Scheme != Uri.UriSchemeHttps)
                {
                    throw new InvalidOperationException("MediaStore:PublicBase must be an absolute https URL.");
                }
                _publicBase = baseUri.AbsoluteUri.TrimEnd('/') + "/";
                return;
            }

            if (string.IsNullOrWhiteSpace(configuration["MediaStore:WebDavBase"]))
            {
                return;
            }
            IsWebDav = true;

            var user = configuration["MediaStore:WebDavUser"];
            var passwordFile = configuration["MediaStore:WebDavPasswordFile"];
            if (string.IsNullOrEmpty(user) || string.IsNullOrEmpty(passwordFile))
            {
                return;
            }
            string password;
            try
            {
                password = File.ReadAllText(passwordFile).TrimEnd('\r', '\n');
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Name the path and the cause, never the content. A 0600 file or a missing mount lands here.
                throw new InvalidOperationException(
                    $"MediaStore:WebDavPasswordFile '{passwordFile}' is not readable: {ex.GetType().Name}", ex);
            }
            WebDavAuthorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{user}:{password}")));
        }

        private readonly string _publicBase;

        /// <summary>True when files come from a local folder.</summary>
        public bool IsLocal => _root != null;

        /// <summary>True when files are on a public https store the player is sent to.</summary>
        public bool IsPublic => _publicBase != null;

        /// <summary>
        /// A catalogue path ("films/sintel/sintel-1080p.mp4") as its address on the public store, each segment escaped,
        /// or null when there is no public store or the path is rooted, empty or climbs ("..").
        /// </summary>
        public string PublicUrl(string filePath)
        {
            if (_publicBase == null || string.IsNullOrEmpty(filePath) || Path.IsPathRooted(filePath))
            {
                return null;
            }
            var segments = filePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s is "." or ".."))
            {
                return null;
            }
            return _publicBase + string.Join('/', segments.Select(Uri.EscapeDataString));
        }

        /// <summary>True when files come from the WebDAV store.</summary>
        public bool IsWebDav { get; }

        /// <summary>The Authorization header for the WebDAV store, or null when it is read without one.</summary>
        public AuthenticationHeaderValue WebDavAuthorization { get; }

        /// <summary>
        /// A catalogue path ("media/one.mp4") as a full local path under the root, or null when there is no root or the
        /// path would leave it.
        /// </summary>
        public string LocalPath(string filePath)
        {
            if (_root == null || string.IsNullOrEmpty(filePath) || Path.IsPathRooted(filePath))
            {
                return null;
            }
            var full = Path.GetFullPath(Path.Combine(_root, filePath.Replace('/', Path.DirectorySeparatorChar)));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return full.StartsWith(_root, comparison) ? full : null;
        }
    }
}
