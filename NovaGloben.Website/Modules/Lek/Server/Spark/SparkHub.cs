using Microsoft.AspNetCore.SignalR;

using Lek.Server.Services;

namespace Lek.Server.Spark
{
    /// <summary>The Spark hub: membership, inputs and actions in; the loop broadcasts "state" to the room's group.</summary>
    public sealed class SparkHub : LekHub
    {
        private readonly SparkWorld _world;

        public SparkHub(SparkWorld world)
        {
            _world = world;
        }

        public async Task<object> CreateRoom(string? nickname)
        {
            await LeaveCurrent();
            return await Enter(_world.CreateRoom(), nickname);
        }

        public async Task<object> JoinRoom(string code, string? nickname)
        {
            var room = _world.Find(code);
            if (room == null) return new { error = "No room with that code." };
            await LeaveCurrent();
            return await Enter(room, nickname);
        }

        public Task Leave() => LeaveCurrent();

        /// <summary>Movement and aim, sent on change and once a second; fire and ability are edge-triggered presses.</summary>
        public void Input(float dx, float dy, float aim)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync)
            {
                player.InputX = Math.Clamp(dx, -1f, 1f);
                player.InputY = Math.Clamp(dy, -1f, 1f);
                if (!float.IsNaN(aim)) player.Aim = aim;
                room.LastActivity = DateTime.UtcNow;
            }
        }

        public void Fire(float aim)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) { if (!float.IsNaN(aim)) player.Aim = aim; player.WantFire = true; }
        }

        public void Ability(float aim)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) { if (!float.IsNaN(aim)) player.Aim = aim; player.WantAbility = true; }
        }

        public object SetKind(string kind)
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync) { var error = room.SetKind(player.Id, kind); return error == null ? new { ok = true } : new { error }; }
        }

        public object Start()
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can start." };
                var error = room.Start();
                return error == null ? new { ok = true } : new { error };
            }
        }

        public object AddBot()
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can add bots." };
                if (room.Players.Count >= SparkRoom.MaxPlayers) return new { error = "The room is full." };
                room.AddBot();
                return new { ok = true };
            }
        }

        public object RemoveBot()
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can remove bots." };
                return room.RemoveLastBot() ? new { ok = true } : new { error = "No bots to remove." };
            }
        }

        public double Ping(double stamp) => stamp;

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await LeaveCurrent();
            await base.OnDisconnectedAsync(exception);
        }

        private async Task<object> Enter(SparkRoom room, string? nickname)
        {
            var name = ResolveName(nickname);
            var id = Guid.NewGuid().ToString("N")[..8];
            lock (room.Sync)
            {
                if (room.Players.Count >= SparkRoom.MaxPlayers) return new { error = "The room is full (6 players)." };
                room.AddPlayer(id, name, false);
            }
            _world.Connections[Context.ConnectionId] = (room.Code, id);
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            return new
            {
                code = room.Code,
                playerId = id,
                name,
                tile = SparkMap.Tile,
                map = room.Map.Rows,
                kinds = SparkKind.All.Select(item => new { id = item.Id, name = item.Name, element = item.Element, primary = item.Primary, ability = item.Ability, cd1 = item.PrimaryCooldown, cd2 = item.AbilityCooldown, windup = item.Windup }).ToArray()
            };
        }

        private async Task LeaveCurrent()
        {
            if (!_world.Connections.TryRemove(Context.ConnectionId, out var seat)) return;
            if (_world.Rooms.TryGetValue(seat.RoomCode, out var room))
            {
                lock (room.Sync) { room.RemovePlayer(seat.PlayerId); }
            }
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, seat.RoomCode);
        }

        private bool TryGetPlayer(out SparkRoom room, out SparkPlayer player)
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
