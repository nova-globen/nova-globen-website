using System.Collections.Generic;

namespace Didin.Shared.Models
{
    /// <summary>
    /// "Ask Didin": the user says what they are in the mood for; the advisor answers with 10 titles from the
    /// catalog, chosen with the user's own ratings and history. The conversation lives in the page; each request carries it.
    /// </summary>
    public class AdvisorRequest
    {
        public string Message { get; set; }                         // what the user just wrote (may be empty for "show 10 others")
        public List<AdvisorTurn> History { get; set; } = new();     // the conversation so far, oldest first
        public List<int> Exclude { get; set; } = new();             // titles already offered or turned down: not again
    }

    public class AdvisorTurn
    {
        public string Role { get; set; }                            // "user" | "assistant"
        public string Text { get; set; }
    }

    public class AdvisorReply
    {
        public string Reply { get; set; }                           // a sentence or two from the advisor
        public List<AdvisorPick> Picks { get; set; } = new();       // up to 10, best first
        public string Error { get; set; }                           // shown instead of a reply (not set up, quota, rate limit ...)
    }

    public class AdvisorPick
    {
        public int TitleId { get; set; }
        public string Reason { get; set; }
    }

    public class AdvisorStatus
    {
        public bool Available { get; set; }
        public string Reason { get; set; }                          // why not, when it is not
    }
}
