using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Didin.Server.Documents;
using Didin.Shared.Models;

namespace Didin.Server.Services
{
    /// <summary>
    /// The subtitle tracks a file offers, as the player's menus show them. A language with one track is just
    /// "Persian". Several tracks in one language are told apart by their file names: the part they do not share with
    /// each other ("Persian · 720p BluRay"), with the track whose name is closest to the video's marked
    /// Best, which is also the one a remembered language picks. Each track keeps its index in the file's list, which
    /// is what /api/subs/{token}/{index} serves.
    /// </summary>
    public static class SubtitleLabels
    {
        private const int MaxDetail = 40;
        private static readonly Regex Year = new(@"^(19|20)\d\d$", RegexOptions.Compiled);
        private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
        {
            "fa", "farsi", "persian", "per", "fas", "en", "eng", "english", "tr", "tur", "turkish",
            "sub", "subs", "subtitle", "subtitles", "srt", "vtt", "-", "&"
        };

        public static List<SubtitleTrack> Describe(string videoPath, IReadOnlyList<SubtitleRef> subs, Func<int, string> url)
        {
            var result = new List<SubtitleTrack>();
            if (subs == null || subs.Count == 0) return result;
            var video = Words(FileStem(videoPath));
            var items = subs.Select((s, i) => (Sub: s, Index: i, Name: IsEmbedded(s) ? EmbeddedWords(s) : Words(FileStem(s.Path)))).ToList();

            foreach (var group in items.GroupBy(x => string.IsNullOrEmpty(x.Sub.Lang) ? "und" : x.Sub.Lang))
            {
                var language = SubtitleText.LanguageName(group.Key);
                var list = group.ToList();
                if (list.Count == 1)
                {
                    result.Add(new SubtitleTrack { Url = url(list[0].Index), Lang = group.Key, Label = language, Best = true });
                    continue;
                }
                var shared = SharedPrefix(list.Select(x => x.Name).ToList());
                var ranked = list
                    .Select(x => (Item: x, Detail: IsEmbedded(x.Sub) ? string.Join(' ', x.Name) : Detail(x.Name, shared),
                                  Score: IsEmbedded(x.Sub) ? 2.0 : Similarity(x.Name, video)))
                    .OrderByDescending(x => x.Score)
                    .ThenBy(x => x.Item.Index)
                    .ToList();
                var clearWinner = ranked[0].Score >= 0.5 && ranked[0].Score > ranked[1].Score;
                for (var n = 0; n < ranked.Count; n++)
                {
                    var detail = ranked[n].Detail;
                    if (detail.Length == 0) detail = string.Join(' ', ranked[n].Item.Name);
                    if (detail.Length == 0) detail = (n + 1).ToString();
                    result.Add(new SubtitleTrack
                    {
                        Url = url(ranked[n].Item.Index),
                        Lang = group.Key,
                        Label = $"{language} · {Shorten(detail)}",
                        Best = n == 0 && clearWinner
                    });
                }
            }

            // The browser's own menu lists labels only, so they must differ.
            var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var track in result)
            {
                seen[track.Label] = seen.TryGetValue(track.Label, out var count) ? count + 1 : 1;
                if (seen[track.Label] > 1) track.Label = $"{track.Label} ({seen[track.Label]})";
            }
            return result;
        }

        // A track from inside the video itself: always in sync with it.
        private static bool IsEmbedded(SubtitleRef s) => s.Path?.StartsWith("embedded:", StringComparison.Ordinal) == true;

        private static List<string> EmbeddedWords(SubtitleRef s)
        {
            var words = new List<string> { "built-in" };
            if (!string.IsNullOrWhiteSpace(s.Label))
            {
                words.AddRange(Words(s.Label).Where(w => !Noise.Contains(w)));
            }
            return words;
        }

        private static string FileStem(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            var name = path.Replace('\\', '/');
            name = name[(name.LastIndexOf('/') + 1)..];
            var dot = name.LastIndexOf('.');
            return dot > 0 ? name[..dot] : name;
        }

        // "Example.Film.2001.720p.BluRay.x264-GROUP (Source)" -> Example, Film, 2001, 720p, BluRay, x264-GROUP, (Source)
        private static List<string> Words(string stem) =>
            stem.Replace('.', ' ').Replace('_', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

        // The leading words every same-language track shares (usually the title) say nothing about which is which.
        private static int SharedPrefix(List<List<string>> names)
        {
            var n = 0;
            while (names.All(x => x.Count > n) &&
                   names.All(x => string.Equals(x[n], names[0][n], StringComparison.OrdinalIgnoreCase)))
            {
                n++;
            }
            return n;
        }

        private static string Detail(List<string> name, int shared) =>
            string.Join(' ', name.Skip(shared).Where(w => !Year.IsMatch(w) && !Noise.Contains(w))).Trim(' ', '-', '.', '&');

        // Word overlap with the video's own file name (hyphenated tags count by part): the track cut for this release
        // scores highest.
        private static double Similarity(List<string> a, List<string> b)
        {
            static HashSet<string> Set(List<string> words) => words
                .SelectMany(w => w.Split('-', '(', ')', '[', ']'))
                .Select(w => w.Trim().ToLowerInvariant())
                .Where(w => w.Length > 0 && !Noise.Contains(w))
                .ToHashSet();
            var x = Set(a);
            var y = Set(b);
            if (x.Count == 0 || y.Count == 0) return 0;
            return (double)x.Intersect(y).Count() / x.Union(y).Count();
        }

        private static string Shorten(string text) =>
            text.Length <= MaxDetail ? text : text[..(MaxDetail - 1)].TrimEnd() + "…";
    }
}
