using System.Collections.Generic;
using Didin.Shared.Models;

namespace Didin.Module.Media.Services
{
    /// <summary>
    /// One "Ask Didin" conversation, kept by the catalog page so it survives opening a title and coming back.
    /// Lost on a reload, by design: nothing of it is stored on the server.
    /// </summary>
    public class AdvisorConversation
    {
        public List<AdvisorTurn> Turns { get; } = new();          // what was said, oldest first (sent back with each request)
        public List<AdvisorPick> Picks { get; set; } = new();      // the current ten
        public HashSet<int> Offered { get; } = new();              // everything suggested so far: never offered twice
        public Dictionary<int, string> Outcome { get; } = new();   // titleId -> "seen" | "notforme"
        public string Error { get; set; }

        public void Clear()
        {
            Turns.Clear();
            Picks = new();
            Offered.Clear();
            Outcome.Clear();
            Error = null;
        }
    }
}
