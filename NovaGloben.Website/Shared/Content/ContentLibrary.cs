using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;

namespace NovaGloben.Website.Content
{
    /// <summary>
    /// The sites' page content: the files under the repository's content/sites/, compiled into this assembly (see the
    /// Shared project file), so what renders is exactly what the repository holds at the commit that was built. A key is
    /// the file's path below content/sites/ with forward slashes, for example "company-en/pages/home.html".
    /// </summary>
    public static class ContentLibrary
    {
        private const string Prefix = "content/sites/";
        private static readonly Assembly Assembly = typeof(ContentLibrary).Assembly;
        private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.Ordinal);

        /// <summary>The file's text, or null when the key names no file.</summary>
        public static string Get(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }
            return Cache.GetOrAdd(Normalise(key), Load);
        }

        /// <summary>Every key, for the import and the tests.</summary>
        public static string[] Keys() =>
            Assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
                .Select(name => name.Substring(Prefix.Length))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray();

        private static string Normalise(string key) => key.Replace('\\', '/').TrimStart('/');

        private static string Load(string key)
        {
            using var stream = Assembly.GetManifestResourceStream(Prefix + key);
            if (stream == null)
            {
                return null;
            }
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
