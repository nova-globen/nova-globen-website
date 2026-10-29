using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Didin.Shared.Models;

namespace Didin.Module.Media.Services
{
    /// <summary>Filter criteria - combinable; empty means "no restriction".</summary>
    public class MediaFilter
    {
        public string Text { get; set; }
        public HashSet<string> Types { get; set; } = new();
        public HashSet<string> Genres { get; set; } = new();
        public HashSet<string> Qualities { get; set; } = new();
        public HashSet<string> Languages { get; set; } = new();
        public HashSet<string> Collections { get; set; } = new();
        public HashSet<string> Ratings { get; set; } = new();
        public int? YearMin { get; set; }
        public int? YearMax { get; set; }
        public int? RuntimeMin { get; set; }
        public int? RuntimeMax { get; set; }
        public double? ImdbMin { get; set; }
        public string Sort { get; set; } = "title";   // title | year | rating | rt | runtime

        public bool IsEmpty => string.IsNullOrWhiteSpace(Text) && NoCriteriaButText;

        /// <summary>Nothing set besides (maybe) the search text.</summary>
        public bool NoCriteriaButText =>
            Types.Count == 0 && Genres.Count == 0 &&
            Qualities.Count == 0 && Languages.Count == 0 && Collections.Count == 0 &&
            Ratings.Count == 0 && YearMin == null && YearMax == null &&
            RuntimeMin == null && RuntimeMax == null && ImdbMin == null;

        // Folded search text per card and per query - the facet counts run the filter several times per render.
        private static readonly ConditionalWeakTable<CatalogCard, string> SearchIndex = new();
        private string _queryFor;
        private string _query = "";

        public bool Matches(CatalogCard c)
        {
            if (!string.IsNullOrWhiteSpace(Text))
            {
                if (_queryFor != Text)
                {
                    _query = SearchText.Normalize(Text);
                    _queryFor = Text;
                }
                var haystack = SearchIndex.GetValue(c, card => SearchText.Normalize(
                    $"{card.Title} {card.OriginalTitle} {card.Collection} {string.Join(' ', card.Genres)}"));
                if (_query.Length > 0 && !SearchText.Contains(haystack, _query)) return false;
            }
            if (Types.Count > 0 && !Types.Contains(c.Kind)) return false;
            if (Genres.Count > 0 && !c.Genres.Any(g => Genres.Contains(g))) return false;
            if (Qualities.Count > 0 && (c.Quality == null || !Qualities.Contains(c.Quality))) return false;
            if (Languages.Count > 0 && !c.Languages.Any(l => Languages.Contains(l))) return false;
            if (Collections.Count > 0 && (c.Collection == null || !Collections.Contains(c.Collection))) return false;
            if (Ratings.Count > 0 && (c.ContentRating == null || !Ratings.Contains(c.ContentRating))) return false;
            if (YearMin != null && (c.Year == null || c.Year < YearMin)) return false;
            if (YearMax != null && (c.Year == null || c.Year > YearMax)) return false;
            if (RuntimeMin != null && (c.RuntimeMinutes == null || c.RuntimeMinutes < RuntimeMin)) return false;
            if (RuntimeMax != null && (c.RuntimeMinutes == null || c.RuntimeMinutes > RuntimeMax)) return false;
            if (ImdbMin != null && (c.ImdbRating == null || c.ImdbRating < ImdbMin)) return false;
            return true;
        }
    }

    /// <summary>Client-side app state: cached catalog, filters, current view.</summary>
    public class MediaState
    {
        public CatalogDocument Catalog { get; private set; } = new();
        public UserState User { get; private set; } = new();   // the user's list and part-watched files
        public MediaFilter Filter { get; set; } = new();
        public int? SelectedTitleId { get; set; }
        public int Limit { get; private set; } = 120;
        public const int Page = 120;
        public event System.Action Changed;

        public void ShowMore()
        {
            Limit += Page;
            Notify();
        }

        public void SetUserState(UserState state)
        {
            User = state ?? new UserState();
            Notify();
        }

        public bool OnWatchlist(int titleId) => User.Watchlist.Contains(titleId);

        // ---- seen: out of every list but search and Watched ----
        public bool IsSeen(int titleId) => User.Seen.Contains(titleId);

        /// <summary>Optimistic, like the watchlist: the lists change at once; the server write follows.</summary>
        public void MarkSeen(int titleId, bool seen)
        {
            User.Seen.Remove(titleId);
            if (seen) User.Seen.Insert(0, titleId);
            User.Finished.RemoveAll(f => f.TitleId == titleId);
            Notify();
        }

        public void DropFinished(int titleId)
        {
            User.Finished.RemoveAll(f => f.TitleId == titleId);
            Notify();
        }

        /// <summary>
        /// This device cannot play MKV files: iPhone and iPad (every browser there is Safari's engine) and Safari
        /// on a Mac. Set once by the host from the browser itself. Such a device gets a notice instead of a player for an
        /// MKV file; a file with an MP4 or a converted copy plays as usual.
        /// </summary>
        public bool NoMatroska { get; set; }

        public bool CannotPlayHere(MediaFileInfo file) =>
            NoMatroska && file != null && file.Playback != "converted" &&
            (file.FileName ?? "").EndsWith(".mkv", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>The subtitle language the player turns on by itself; "off", or null until chosen.</summary>
        public void SetSubtitleLanguage(string lang) => User.SubtitleLang = lang;

        public void MarkWatchlist(int titleId, bool onList)
        {
            User.Watchlist.Remove(titleId);
            if (onList) User.Watchlist.Insert(0, titleId);
            Notify();
        }

        /// <summary>Where the user stopped in this file, if part-way (seconds), else null.</summary>
        public double? ResumeAt(string fileToken) =>
            User.Continue.FirstOrDefault(c => c.File?.Token == fileToken) is { } item ? item.Position : null;

        public void SetCatalog(CatalogDocument doc)
        {
            Catalog = doc ?? new CatalogDocument();
            Notify();
        }

        public void Select(int? titleId)
        {
            SelectedTitleId = titleId;
            Notify();
        }

        public void ReplaceFilter(MediaFilter filter)
        {
            Filter = filter ?? new MediaFilter();
            Notify();
        }

        /// <summary>
        /// Live facet values with counts over the currently filtered result
        /// set: values with count 0 sort to the end of the list.
        /// </summary>
        public List<(string Value, int Count)> FacetCounts(string which)
        {
            var res = Filtered();
            IEnumerable<(string, int)> pairs = which switch
            {
                "type" => res.GroupBy(c => c.Kind).Select(g => (g.Key, g.Count())),
                "genre" => res.SelectMany(c => c.Genres).GroupBy(x => x).Select(g => (g.Key, g.Count())),
                "quality" => res.Where(c => c.Quality != null).GroupBy(c => c.Quality!).Select(g => (g.Key, g.Count())),
                "language" => res.SelectMany(c => c.Languages).GroupBy(x => x).Select(g => (g.Key, g.Count())),
                "collection" => res.Where(c => c.Collection != null).GroupBy(c => c.Collection!).Select(g => (g.Key, g.Count())),
                "rating" => res.Where(c => c.ContentRating != null).GroupBy(c => c.ContentRating!).Select(g => (g.Key, g.Count())),
                _ => Enumerable.Empty<(string, int)>(),
            };
            var known = which switch
            {
                "type" => Catalog.Facets.Types,
                "genre" => Catalog.Facets.Genres,
                "quality" => Catalog.Facets.Qualities,
                "language" => Catalog.Facets.Languages,
                "collection" => Catalog.Facets.Collections,
                "rating" => Catalog.Facets.Ratings,
                _ => new List<string>(),
            };
            var counts = pairs.ToDictionary(p => p.Item1, p => p.Item2);
            return known
                .Select(v => (Value: v, Count: counts.TryGetValue(v, out var n) ? n : 0))
                .OrderBy(f => f.Count == 0)
                .ThenBy(f => f.Value, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<CatalogCard> Filtered()
        {
            var q = Catalog.Cards.Where(Filter.Matches);
            q = Filter.Sort switch
            {
                "year" => q.OrderByDescending(c => c.Year ?? 0),
                "rating" => q.OrderByDescending(c => c.ImdbRating ?? 0),
                "rt" => q.OrderByDescending(c => c.RottenTomatoes ?? -1),
                "runtime" => q.OrderByDescending(c => c.RuntimeMinutes ?? 0),
                _ => q.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase),
            };
            return q.ToList();
        }

        public void Notify(bool filtersChanged = false)
        {
            if (filtersChanged) Limit = Page;
            Changed?.Invoke();
        }
    }
}
