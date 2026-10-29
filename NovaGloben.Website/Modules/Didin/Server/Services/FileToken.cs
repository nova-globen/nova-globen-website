using System;
using System.Text;

namespace Didin.Server.Services
{
    /// <summary>
    /// A file's address in Didin's URLs: its document id ("files/123") as base64url. Only a file the catalogue lists has
    /// one, so a stream or subtitle address can name nothing else.
    /// </summary>
    public static class FileToken
    {
        public static string Encode(string value) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>The document id a token names, or null when it is not a token.</summary>
        public static string Decode(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }
            try
            {
                var s = token.Replace('-', '+').Replace('_', '/');
                s += new string('=', (4 - s.Length % 4) % 4);
                return Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }
            catch (FormatException)
            {
                return null;
            }
        }
    }
}
