using System;
using System.Collections.Generic;

namespace Didin.Shared.Models
{
    /// <summary>Slim card payload for the browse grid; the whole catalog ships as one cached JSON.</summary>
    public class CatalogCard
    {
        public int Id { get; set; }
        public string Kind { get; set; }           // movie | series | short | music_video | other
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public int? Year { get; set; }
        public int? EndYear { get; set; }
        public string PosterUrl { get; set; }
        public bool PosterWide { get; set; }       // wide art (a still, key art): shown whole, not cropped to the card
        public List<string> Genres { get; set; } = new();
        public int? RuntimeMinutes { get; set; }
        public string ContentRating { get; set; }
        public double? ImdbRating { get; set; }
        public int? RottenTomatoes { get; set; }   // Tomatometer, percent
        public string Quality { get; set; }        // best available file quality
        public List<string> Languages { get; set; } = new();
        public string Collection { get; set; }     // franchise name, if any
        public int? CollectionOrder { get; set; }
        public int EpisodeCount { get; set; }
        public int FileCount { get; set; }
    }

    public class CatalogFacets
    {
        public List<string> Genres { get; set; } = new();
        public List<string> Types { get; set; } = new();
        public List<string> Qualities { get; set; } = new();
        public List<string> Languages { get; set; } = new();
        public List<string> Collections { get; set; } = new();
        public List<string> Ratings { get; set; } = new();
        public int? MinYear { get; set; }
        public int? MaxYear { get; set; }
        public int? MinRuntime { get; set; }
        public int? MaxRuntime { get; set; }
    }

    /// <summary>Full catalog document: version + cards + facets. Cached client-side.</summary>
    public class CatalogDocument
    {
        public string Version { get; set; }
        public DateTime GeneratedUtc { get; set; }
        public int TitleCount { get; set; }
        public List<CatalogCard> Cards { get; set; } = new();
        public CatalogFacets Facets { get; set; } = new();
        public List<CatalogCredit> Credits { get; set; } = new();   // whose licensed work the catalogue lists
    }

    /// <summary>A licensor of titles the catalogue lists, and the licences they are under: the catalogue's credits line.</summary>
    public class CatalogCredit
    {
        public string Licensor { get; set; }
        public string LicensorUrl { get; set; }
        public List<CatalogLicense> Licenses { get; set; } = new();
    }

    public class CatalogLicense
    {
        public string Name { get; set; }
        public string Url { get; set; }
    }

    /// <summary>One person's titles in the catalog: what the title page's names open.</summary>
    public class PersonTitles
    {
        public string Name { get; set; }
        public List<string> Roles { get; set; } = new();           // actor, director, writer, producer, composer, creator
        public List<PersonTitle> Titles { get; set; } = new();
    }

    public class PersonTitle
    {
        public int TitleId { get; set; }
        public List<string> Roles { get; set; } = new();
        public string Character { get; set; }
    }

    public class PersonCredit
    {
        public string Name { get; set; }
        public string Role { get; set; }
        public string Character { get; set; }
        public int? Billing { get; set; }
    }

    public class EpisodeInfo
    {
        public int Id { get; set; }
        public int Season { get; set; }
        public int Number { get; set; }
        public int? NumberEnd { get; set; }
        public string Name { get; set; }
        public string AirDate { get; set; }
        public string Plot { get; set; }
        public int? RuntimeMinutes { get; set; }
        public double? Rating { get; set; }
        public List<MediaFileInfo> Files { get; set; } = new();
    }

    public class SeasonInfo
    {
        public int Season { get; set; }
        public List<EpisodeInfo> Episodes { get; set; } = new();
    }

    public class MediaFileInfo
    {
        public string Token { get; set; }          // base64url file key for /api/stream/{token}
        public string FileName { get; set; }
        public string Quality { get; set; }
        public string Source { get; set; }
        public string Codec { get; set; }
        public string Audio { get; set; }
        public string Edition { get; set; }
        public string Languages { get; set; }
        public long SizeBytes { get; set; }
        public int? Part { get; set; }
        public int? PartCount { get; set; }
        public string WebDavUrl { get; set; }      // direct LAN playback
        public List<SubtitleTrack> Subtitles { get; set; } = new();
        public string Playback { get; set; }   // native | audio | transcode | damaged | converted | null (not probed)
        public string Codecs { get; set; }
        public string Role { get; set; }              // null = the title itself | "extra" | "menu" (DVD segments, hidden)
        public double? DurationSeconds { get; set; }
    }

    /// <summary>What the signed-in user has going: their list and the files they are part-way through.</summary>
    public class UserState
    {
        public bool IsAdmin { get; set; }   // shows the Library view
        public string SubtitleLang { get; set; }   // "fa", "en", ... or "off"; null until the user chooses
        public List<int> Watchlist { get; set; } = new();
        public List<ContinueItem> Continue { get; set; } = new();
        public List<int> Seen { get; set; } = new();                // hidden from every list but search and Watched, newest first
        public List<FinishedItem> Finished { get; set; } = new();   // played to the end, not yet rated, skipped or seen
    }

    /// <summary>A title the user played to the end (a film's last part, a series' last episode) - "Finished watching".</summary>
    public class FinishedItem
    {
        public int TitleId { get; set; }
        public DateTime FinishedUtc { get; set; }
    }

    public class ContinueItem
    {
        public int TitleId { get; set; }
        public string Title { get; set; }
        public string Label { get; set; }          // "S02E05 - Name" for an episode, else null
        public MediaFileInfo File { get; set; }
        public double Position { get; set; }
        public double Duration { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>The library's health for administrators.</summary>
    public class LibraryReport
    {
        public string CatalogVersion { get; set; }
        public DateTime SeededUtc { get; set; }
        public int Titles { get; set; }
        public int Files { get; set; }
        public int Posters { get; set; }
        public int FilesWithSubtitles { get; set; }
        public Dictionary<string, int> Playback { get; set; } = new();   // class -> files
        public int Users { get; set; }                                   // users with saved state
        public int Ratings { get; set; }
        public List<LibraryFile> ReviewQueue { get; set; } = new();      // index status "unknown"
        public List<LibraryFile> Damaged { get; set; } = new();          // probe class "damaged"
        public List<LibraryFile> Missing { get; set; } = new();          // gone from the share (index status "missing")
    }

    public class LibraryFile
    {
        public string Path { get; set; }
        public string Title { get; set; }
        public string Notes { get; set; }
        public string Token { get; set; }           // the file's API token (review verdicts)
        public ReviewVerdict Verdict { get; set; }  // review queue only; null = no verdict yet
    }

    /// <summary>An administrator's verdict on an unidentified file; see ReviewVerdictDoc.</summary>
    public class ReviewVerdict
    {
        public string Verdict { get; set; }     // ignore | title | note
        public int? TitleId { get; set; }
        public string Title { get; set; }       // "Title (year)" of TitleId, for display
        public string Note { get; set; }
        public string UserName { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public class ReviewVerdictUpdate
    {
        public string Verdict { get; set; }
        public int? TitleId { get; set; }
        public string Note { get; set; }
    }

    /// <summary>What the site's users think of a title.</summary>
    public class TitleRatings
    {
        public double? Average { get; set; }
        public int Count { get; set; }
        public RatingInfo Mine { get; set; }
        public List<RatingInfo> Reviews { get; set; } = new();   // everyone's, newest first, with text only
    }

    public class RatingInfo
    {
        public string User { get; set; }
        public int Stars { get; set; }
        public string Review { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    public class RatingUpdate
    {
        public int Stars { get; set; }
        public string Review { get; set; }
    }

    public class ProgressUpdate
    {
        public double Position { get; set; }
        public double Duration { get; set; }
    }

    /// <summary>A subtitle the player can load: WebVTT from /api/subs/{token}/{index}.</summary>
    /// <summary>
    /// What a cast device (Chromecast / Google TV) needs to play one file: server-relative URLs carrying a
    /// ticket instead of the user's sign-in, to be prefixed with the site's public address.
    /// </summary>
    public class CastMedia
    {
        public string Url { get; set; }
        public string ContentType { get; set; }
        public List<CastSubtitle> Subtitles { get; set; } = new();
    }

    public class CastSubtitle
    {
        public string Url { get; set; }
        public string Lang { get; set; }
        public string Label { get; set; }
    }

    public class SubtitleTrack
    {
        public string Url { get; set; }
        public string Lang { get; set; }      // ISO 639-1 ("fa", "en", "tr") or "und"
        public string Label { get; set; }     // "Persian", or "Persian · 720p BluRay DTS" when a language has several
        public bool Best { get; set; }        // the track a remembered language picks: the closest to the video's file name
    }

    /// <summary>The user's subtitle choice: a language code, or "off".</summary>
    public class SubtitlePreference
    {
        public string Lang { get; set; }
    }

    public class RelatedTitle
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public string PosterUrl { get; set; }
        public bool PosterWide { get; set; }
        public string Collection { get; set; }
        public double? CollectionOrder { get; set; }
        public string Reason { get; set; }   // "More like this": why it was picked
    }

    /// <summary>Details page payload (movie or series).</summary>
    public class TitleDetails
    {
        public int Id { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string OriginalTitle { get; set; }
        public int? Year { get; set; }
        public int? EndYear { get; set; }
        public string Tagline { get; set; }
        public string Plot { get; set; }
        public string PlotFull { get; set; }
        public int? RuntimeMinutes { get; set; }
        public string ContentRating { get; set; }
        public List<string> Genres { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        public List<string> Languages { get; set; } = new();
        public List<string> Countries { get; set; } = new();
        public double? ImdbRating { get; set; }
        public int? RottenTomatoes { get; set; }   // Tomatometer, percent
        public int? Metascore { get; set; }
        public string TrailerUrl { get; set; }
        public string Awards { get; set; }
        public string BoxOffice { get; set; }
        public string Budget { get; set; }
        public string Network { get; set; }
        public string Licensor { get; set; }
        public string LicensorUrl { get; set; }
        public string Attribution { get; set; }   // the credit line the licensor asks for
        public string License { get; set; }       // "CC BY 3.0"
        public string LicenseUrl { get; set; }    // the licence deed
        public string SourceUrl { get; set; }     // the page the licence is stated on
        public string PosterUrl { get; set; }
        public bool PosterWide { get; set; }
        public string WikipediaUrl { get; set; }
        public string ImdbUrl { get; set; }
        public List<PersonCredit> People { get; set; } = new();
        public List<MediaFileInfo> Files { get; set; } = new();   // movies/shorts
        public List<SeasonInfo> Seasons { get; set; } = new();    // series
        public List<RelatedTitle> Related { get; set; } = new();  // franchise row
        public List<RelatedTitle> Recommended { get; set; } = new();  // "More like this"
    }
}
