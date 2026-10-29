using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Lek.Server.Noir
{
    /// <summary>All the Bullet Noir rooms on this host, in memory, and which connection sits in which room.</summary>
    public sealed class NoirWorld
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public ConcurrentDictionary<string, NoirRoom> Rooms { get; } = new();
        public ConcurrentDictionary<string, (string RoomCode, string PlayerId)> Connections { get; } = new();

        /// <summary>The last play-test lines from clients (mouse and lock state), newest last, for reading back after a session.</summary>
        public ConcurrentQueue<string> Diags { get; } = new();

        public void AddDiag(string line)
        {
            Diags.Enqueue(line);
            while (Diags.Count > 400) Diags.TryDequeue(out _);
        }

        public NoirRoom CreateRoom()
        {
            while (true)
            {
                var code = string.Create(4, 0, (span, _) =>
                {
                    for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
                });
                var room = new NoirRoom(code);
                if (Rooms.TryAdd(code, room)) return room;
            }
        }

        public NoirRoom? Find(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return Rooms.TryGetValue(code.Trim().ToUpperInvariant(), out var room) ? room : null;
        }

        public void Sweep(TimeSpan idle)
        {
            foreach (var (code, room) in Rooms)
            {
                bool empty;
                lock (room.Sync) { empty = room.HumanCount == 0 && DateTime.UtcNow - room.LastActivity > idle; }
                if (empty) Rooms.TryRemove(code, out _);
            }
        }
    }
}
