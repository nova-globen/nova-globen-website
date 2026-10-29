using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Lek.Server.Fuse
{
    /// <summary>All the rooms on this host, in memory, and which connection sits in which room.</summary>
    public sealed class FuseWorld
    {
        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public ConcurrentDictionary<string, FuseRoom> Rooms { get; } = new();
        public ConcurrentDictionary<string, (string RoomCode, string PlayerId)> Connections { get; } = new();

        public FuseRoom CreateRoom()
        {
            while (true)
            {
                var code = string.Create(4, 0, (span, _) =>
                {
                    for (var i = 0; i < span.Length; i++)
                    {
                        span[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
                    }
                });
                var room = new FuseRoom(code);
                if (Rooms.TryAdd(code, room)) return room;
            }
        }

        public FuseRoom? Find(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            return Rooms.TryGetValue(code.Trim().ToUpperInvariant(), out var room) ? room : null;
        }

        /// <summary>Rooms with no humans for a while are dropped.</summary>
        public void Sweep(TimeSpan idle)
        {
            foreach (var (code, room) in Rooms)
            {
                bool empty;
                lock (room.Sync)
                {
                    empty = room.HumanCount == 0 && DateTime.UtcNow - room.LastActivity > idle;
                }
                if (empty) Rooms.TryRemove(code, out _);
            }
        }
    }
}
