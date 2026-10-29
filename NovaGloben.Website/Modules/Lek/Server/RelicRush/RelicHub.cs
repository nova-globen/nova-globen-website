using Microsoft.AspNetCore.SignalR;

using Lek.Server.Services;

namespace Lek.Server.RelicRush
{
    /// <summary>The Relic Rush hub: membership, lobby choices, inputs and the steal and pass presses. Anonymous, like Fuse.</summary>
    public sealed class RelicHub : LekHub
    {
        private readonly RelicWorld _world;

        public RelicHub(RelicWorld world)
        {
            _world = world;
        }

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

        public void Input(int seq, int bits, double aim)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) room.QueueInput(player, seq, bits, double.IsFinite(aim) ? aim : double.NaN);
        }

        public void Steal()
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) player.WantSteal = true;
        }

        public void Pass(double aim)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) { if (double.IsFinite(aim)) player.Aim = aim; player.WantPass = true; }
        }

        public object SetTeam(int team) => Owned(false, (room, player) => room.SetTeam(player.Id, team));
        public object SetMode(int size) => Owned(true, (room, _) => room.SetMode(size));
        public object Start() => Owned(true, (room, _) => room.Start());

        public double Ping(double stamp) => stamp;

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await LeaveCurrent();
            await base.OnDisconnectedAsync(exception);
        }

        private object Owned(bool ownerOnly, Func<RelicRoom, RrPlayer, string?> action)
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (ownerOnly && room.OwnerId != player.Id) return new { error = "Only the room owner can do that." };
                var error = action(room, player);
                return error == null ? new { ok = true } : new { error };
            }
        }

        private object Enter(RelicRoom room, string? nickname)
        {
            var name = ResolveName(nickname);
            var id = Guid.NewGuid().ToString("N")[..8];
            lock (room.Sync)
            {
                if (room.HumanCount >= RelicRoom.MaxPlayers) return new { error = "The room is full (8 players)." };
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

        private bool TryGetPlayer(out RelicRoom room, out RrPlayer player)
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
