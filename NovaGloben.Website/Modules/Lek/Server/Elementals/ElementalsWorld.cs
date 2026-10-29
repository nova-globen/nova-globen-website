using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Lek.Server.Elementals
{
    /// <summary>All the Elementals rooms on this host, in memory, and which connection sits in which room.</summary>
    public sealed class ElementalsWorld
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public ConcurrentDictionary<string, ElementalsRoom> Rooms { get; } = new();
        public ConcurrentDictionary<string, (string RoomCode, string PlayerId)> Connections { get; } = new();

        public ElementalsRoom CreateRoom()
        {
            while (true)
            {
                var code = string.Create(4, 0, (span, _) =>
                {
                    for (var i = 0; i < span.Length; i++) span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
                });
                var room = new ElementalsRoom(code);
                if (Rooms.TryAdd(code, room)) return room;
            }
        }

        public ElementalsRoom? Find(string? code)
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
