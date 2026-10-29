using Microsoft.AspNetCore.SignalR;

using Lek.Server.Services;

namespace Lek.Server.Noir
{
    /// <summary>The Bullet Noir hub: membership, the map, inputs and the reload, weapon and bullet-time presses. Anonymous, like Fuse.</summary>
    public sealed class NoirHub : LekHub
    {
        private readonly NoirWorld _world;
        private readonly ILogger<NoirHub> _logger;

        public NoirHub(NoirWorld world, ILogger<NoirHub> logger)
        {
            _world = world;
            _logger = logger;
        }

        /// <summary>A play-test line from the client (mouse and pointer-lock state), written to the server log.</summary>
        public void Diag(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            var seat = _world.Connections.TryGetValue(Context.ConnectionId, out var s) ? s.RoomCode + "/" + s.PlayerId : "-";
            text = text.Length > 400 ? text[..400] : text;
            _logger.LogInformation("Bullet Noir diag {Seat}: {Text}", seat, text);
            _world.AddDiag($"{DateTime.UtcNow:HH:mm:ss} {seat} {text}");
        }

        /// <summary>The recent play-test lines (no names, only room codes and seat ids), newest last.</summary>
        public string[] RecentDiag(int count) => _world.Diags.TakeLast(Math.Clamp(count, 1, 400)).ToArray();

        public async Task<object> CreateRoom(string? nickname)
        {
            await LeaveCurrent();
            return Enter(_world.CreateRoom(), nickname);
        }

        public async Task<object> JoinRoom(string code, string? nickname)
        {
            var room = _world.Find(code);
            if (room == null) return new { error = "No room with that code." };
            await LeaveCurrent();
            return Enter(room, nickname);
        }

        public Task Leave() => LeaveCurrent();

        public object Map() => NoirMap.Describe();

        public void Input(int seq, int bits, double yaw, double aimYaw, double aimPitch)
        {
            if (!double.IsFinite(yaw) || !double.IsFinite(aimYaw) || !double.IsFinite(aimPitch)) return;
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) room.QueueInput(player, seq, bits, yaw, aimYaw, aimPitch);
        }

        public void Reload() => Press(p => p.WantReload = true);
        public void Weapon(int slot) => Press(p => p.WantSlot = slot == 1 ? 1 : 0);
        public void BulletTime() => Press(p => p.WantBt = true);

        public object SetSize(int size) => Owned((room, _) => room.SetSize(size));
        public object Start() => Owned((room, _) => room.Start());

        public double Ping(double stamp) => stamp;

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await LeaveCurrent();
            await base.OnDisconnectedAsync(exception);
        }

        private void Press(Action<NrPlayer> action)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) action(player);
        }

        private object Owned(Func<NoirRoom, NrPlayer, string?> action)
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can do that." };
                var error = action(room, player);
                return error == null ? new { ok = true } : new { error };
            }
        }

        private object Enter(NoirRoom room, string? nickname)
        {
            var name = ResolveName(nickname);
            var id = Guid.NewGuid().ToString("N")[..8];
            lock (room.Sync)
            {
                if (room.HumanCount >= NoirRoom.MaxPlayers) return new { error = "The room is full (8 players)." };
                room.AddPlayer(id, name, Context.ConnectionId, false);
            }
            _world.Connections[Context.ConnectionId] = (room.Code, id);
            return new { code = room.Code, playerId = id, name };
        }

        private async Task LeaveCurrent()
        {
            if (!_world.Connections.TryRemove(Context.ConnectionId, out var seat)) return;
            if (_world.Rooms.TryGetValue(seat.RoomCode, out var room))
            {
                lock (room.Sync) room.RemovePlayer(seat.PlayerId);
            }
            await Task.CompletedTask;
        }

        private bool TryGetPlayer(out NoirRoom room, out NrPlayer player)
        {
            room = null!;
            player = null!;
            if (!_world.Connections.TryGetValue(Context.ConnectionId, out var seat)) return false;
            if (!_world.Rooms.TryGetValue(seat.RoomCode, out var found)) return false;
            lock (found.Sync)
            {
                if (!found.Players.TryGetValue(seat.PlayerId, out var p)) return false;
                room = found;
                player = p;
                return true;
            }
        }

        private string ResolveName(string? nickname)
        {
            var user = Context.User;
            if (user?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name)) return Trim(user.Identity.Name);
            var name = Trim(nickname ?? "");
            return string.IsNullOrEmpty(name) ? "Guest " + Random.Shared.Next(100, 999) : name;
        }

        private static string Trim(string value)
        {
            value = value.Trim();
            return value.Length > 14 ? value[..14] : value;
        }
    }
}
