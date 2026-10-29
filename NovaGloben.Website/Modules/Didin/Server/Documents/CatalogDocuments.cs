using System;
using System.Collections.Generic;
using Didin.Shared.Models;

namespace Didin.Server.Documents
{
    // The catalogue's documents, stored as JSON in DidinDocument rows (Storage/DidinDocument.cs) and read whole into
    // memory. Their Id keeps the catalogue's own form ("titles/123"); the number after the slash is the row's Number.
    // An import is upsert-only.

    public class TitleDoc
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Title { get; set; }
        public string SortTitle { get; set; }
        public string OriginalTitle { get; set; }
        public int? Year { get; set; }
        public int? EndYear { get; set; }
        public string Tagline { get; set; }
        public string Plot { get; set; }
        public string PlotFull { get; set; }
        public int? RuntimeMinutes { get; set; }
        public string ContentRating { get; set; }
        public string Language { get; set; }
        public List<string> Languages { get; set; } = new();
        public string Country { get; set; }
        public List<string> Countries { get; set; } = new();
        public List<string> Genres { get; set; } = new();
        public List<string> Keywords { get; set; } = new();
        public double? ImdbRating { get; set; }
        public int? ImdbVotes { get; set; }
        public int? Metascore { get; set; }
        public int? RottenTomatoes { get; set; }   // Tomatometer, percent
        public string Awards { get; set; }
        public string BoxOffice { get; set; }
        public string Budget { get; set; }
        public List<string> ProductionCompanies { get; set; } = new();
        public string Network { get; set; }
        public int? SeasonsCount { get; set; }
        public int? EpisodesCount { get; set; }
        public string ShowStatus { get; set; }
        public string PosterUrl { get; set; }
        public string TrailerUrl { get; set; }     // YouTube watch URL
        public string WikipediaUrl { get; set; }
        public string ImdbId { get; set; }
        public string ImdbUrl { get; set; }
        public string Confidence { get; set; }
        public List<PersonCredit> People { get; set; } = new();
        public List<string> Collections { get; set; } = new();   // franchise names
    }

    public class EpisodeDoc
    {
        public string Id { get; set; }
        public int TitleId { get; set; }          // owning series
        public int Season { get; set; }
        public int Episode { get; set; }
        public int? EpisodeEnd { get; set; }
        public string Name { get; set; }
        public string AirDate { get; set; }
        public string Plot { get; set; }
        public int? RuntimeMinutes { get; set; }
        public double? Rating { get; set; }
        public string Director { get; set; }
        public string Writers { get; set; }
        public string GuestStars { get; set; }
    }

    public class FileDoc
    {
        public string Id { get; set; }
        public string FilePath { get; set; }       // the share identifier (path)
        public string FileName { get; set; }
        public int? TitleId { get; set; }
        public int? EpisodeId { get; set; }
        public int? Part { get; set; }
        public int? PartCount { get; set; }
        public string Quality { get; set; }
        public string Source { get; set; }
        public string Codec { get; set; }
        public string Audio { get; set; }
        public string Edition { get; set; }
        public string ReleaseGroup { get; set; }
        public string Languages { get; set; }
        public long SizeBytes { get; set; }
        public string Status { get; set; }
        public List<SubtitleRef> Subtitles { get; set; } = new();   // sidecar subtitle files, set by the import
        public string Notes { get; set; }      // the index's notes - why a file is "unknown" (review queue)
        public string Playback { get; set; }   // native | audio | transcode | damaged | converted (set by the import); null = not probed
        public string PlaybackPath { get; set; }   // share path of the browser-playable copy (set by the import); null = play FilePath
        public string Role { get; set; }       // null = the title itself | "extra" (another DVD title set) | "menu" (a DVD menu segment) - set by the import
        public double? DurationSeconds { get; set; }   // known for converted copies (set by the import)
        public string Codecs { get; set; }     // e.g. "H264 / AAC, 720p"
    }

    /// <summary>
    /// One user's own state: "userstate/{userId}" - the titles on their list and how far they got in each
    /// file. Written by the app at runtime, unlike the seeded catalog, so the database is no longer purely regenerable.
    /// </summary>
    public class UserStateDoc
    {
        public string Id { get; set; }
        public int UserId { get; set; }
        public List<int> Watchlist { get; set; } = new();
        public Dictionary<string, ProgressEntry> Progress { get; set; } = new();   // key: FileDoc id
        public string SubtitleLang { get; set; }   // the user's subtitle choice: a language code or "off"
        public List<SeenMark> Seen { get; set; } = new();           // titles the user has seen - hidden from lists, newest first
        public List<int> FinishedSkipped { get; set; } = new();     // finished titles the user chose not to rate
    }

    public class SeenMark
    {
        public int TitleId { get; set; }
        public DateTime SeenUtc { get; set; }
    }

    /// <summary>
    /// An administrator's verdict on a file the catalogue could not identify: "reviewverdicts/{file number}". Written on
    /// the site's Library page and read by whoever prepares the next catalogue import: "ignore" and "title" verdicts
    /// become catalogue changes there, a "note" verdict only records what the file is. Once an import shows the file no
    /// longer "unknown", its verdict is done.
    /// </summary>
    public class ReviewVerdictDoc
    {
        public string Id { get; set; }
        public string FileId { get; set; }      // the FileDoc id, "files/123"
        public string FilePath { get; set; }
        public string Verdict { get; set; }     // ignore | title | note
        public int? TitleId { get; set; }       // for "title": the catalog title the file is (index titles.id)
        public string Note { get; set; }        // optional; at most 500 characters ("note" needs one)
        public string UserName { get; set; }
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>One user's rating of one title: "ratings/{titleId}-{userId}", queried per title.</summary>
    public class RatingDoc
    {
        public string Id { get; set; }
        public int TitleId { get; set; }
        public int UserId { get; set; }
        public string UserName { get; set; }
        public int Stars { get; set; }        // 1-5
        public string Review { get; set; }    // optional, at most 1,000 characters
        public DateTime UpdatedUtc { get; set; }
    }

    public class ProgressEntry
    {
        public int? TitleId { get; set; }
        public int? EpisodeId { get; set; }
        public double Position { get; set; }   // seconds
        public double Duration { get; set; }   // seconds
        public DateTime UpdatedUtc { get; set; }
    }

    /// <summary>A subtitle file next to a video on the share, and its detected language.</summary>
    public class SubtitleRef
    {
        public string Path { get; set; }      // share identifier, like FileDoc.FilePath; "embedded:{stream}" for a track
                                              // extracted from inside the video (EmbeddedSubtitleDoc)
        public string Lang { get; set; }      // "fa", "en", "tr" or "und"
        public string Label { get; set; }     // what the player's menu shows, e.g. "Persian", "English 2"
    }

    public class CollectionDoc
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public List<CollectionMember> Members { get; set; } = new();
    }

    public class CollectionMember
    {
        public int TitleId { get; set; }
        public double? Order { get; set; }
    }

    public class CatalogVersionDoc
    {
        public string Id { get; set; } = "meta/catalog";
        public string Version { get; set; }
        public DateTime SeededUtc { get; set; }
        public int TitleCount { get; set; }
        public int FileCount { get; set; }
    }

    /// <summary>
    /// A subtitle stream extracted from inside a video: "embeddedsubs/{fileNumber}-{stream}". Its WebVTT text is a file
    /// in the module's cache, named by Hash. Written by the import; separate documents, so a catalogue import never
    /// touches them.
    /// </summary>
    public class EmbeddedSubtitleDoc
    {
        public string Id { get; set; }
        public string FileId { get; set; }     // "files/123"
        public int Stream { get; set; }        // the stream index inside the video
        public string Lang { get; set; }
        public string Hash { get; set; }       // sha256 prefix of the WebVTT bytes: re-uploads only when it changes
    }

    /// <summary>
    /// A cached poster: "posters/{titleId}". The image itself is a file in the module's cache, named by Hash; this
    /// document is apart from the title's so a catalogue import never touches it.
    /// </summary>
    public class PosterDoc
    {
        public string Id { get; set; }
        public int TitleId { get; set; }
        public string SourceUrl { get; set; }    // the title's PosterUrl it was fetched for
        public string FetchedUrl { get; set; }   // what was actually downloaded (a thumbnail for very large originals)
        public string ContentType { get; set; }
        public long Length { get; set; }
        public string Hash { get; set; }         // sha256 prefix of the bytes: the cache key in /api/art URLs
        public DateTime FetchedUtc { get; set; }
    }

}
