using Stomme.Extensions;
using Microsoft.AspNetCore.SignalR;

using Lek.Server.Services;

namespace Lek.Server.Fuse
{
    /// <summary>
    /// The Fuse hub: membership and inputs in, state out (the loop broadcasts "state" to the room's group 20 times a
    /// second). Anonymous connections are allowed: a guest plays under the nickname it sends, a signed-in Stomme user
    /// under the name on its principal.
    /// </summary>
    public sealed class FuseHub : LekHub
    {
        private readonly FuseWorld _world;
        private readonly FuseStats _stats;

        public FuseHub(FuseWorld world, FuseStats stats)
        {
            _world = world;
            _stats = stats;
        }

        public async Task<object> CreateRoom(string? nickname)
        {
            await LeaveCurrent();
            var room = _world.CreateRoom();
            return await Enter(room, nickname);
        }

        public async Task<object> JoinRoom(string code, string? nickname)
        {
            var room = _world.Find(code);
            if (room == null) return new { error = "No room with that code." };
            await LeaveCurrent();
            return await Enter(room, nickname);
        }

        public Task Leave() => LeaveCurrent();

        public void Input(float dx, float dy)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync)
            {
                player.InputX = Math.Clamp(dx, -1f, 1f);
                player.InputY = Math.Clamp(dy, -1f, 1f);
                room.LastActivity = DateTime.UtcNow;
            }
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

        public object SetMap(string id)
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can pick the map." };
                var error = room.SetMap(id);
                return error == null ? new { ok = true } : new { error };
            }
        }

        public object SetOptions(int bombs, bool mystery, bool joker)
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can change options." };
                var error = room.SetOptions(bombs, mystery, joker);
                return error == null ? new { ok = true } : new { error };
            }
        }

        public object Throw()
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                var error = room.Throw(player.Id);
                return error == null ? new { ok = true } : new { error };
            }
        }

        /// <summary>Round trip for the client's ping display: echoes the client's stamp.</summary>
        public double Ping(double stamp) => stamp;

        public object AddBot()
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (room.OwnerId != player.Id) return new { error = "Only the room owner can add bots." };
                if (room.Players.Count >= FuseRoom.MaxPlayers) return new { error = "The room is full." };
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

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await LeaveCurrent();
            await base.OnDisconnectedAsync(exception);
        }

        private async Task<object> Enter(FuseRoom room, string? nickname)
        {
            var name = ResolveName(nickname);
            var id = Guid.NewGuid().ToString("N")[..8];
            var userId = int.TryParse(Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
            var tenantId = 0; var siteId = 0;
            FuseStats.Record? stats = null;
            if (userId != null)
            {
                try
                {
                    tenantId = Context.User!.TenantId();
                    siteId = Context.User!.SiteId();
                    stats = await _stats.LoadAsync(tenantId, siteId, userId.Value);
                }
                catch (Exception)
                {
                    userId = null; // no site on the principal, or the store refused: play as a guest rather than fail
                }
            }
            lock (room.Sync)
            {
                if (room.Players.Count >= FuseRoom.MaxPlayers) return new { error = "The room is full (10 players)." };
                var player = room.AddPlayer(id, name, Context.ConnectionId, false);
                player.UserId = userId;
                player.TenantId = tenantId;
                player.SiteId = siteId;
                if (stats != null) { player.TotalWins = stats.Wins; player.TotalMatches = stats.Matches; }
            }
            _world.Connections[Context.ConnectionId] = (room.Code, id);
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            return new
            {
                code = room.Code,
                playerId = id,
                name,
                tile = FuseMap.Tile,
                maps = FuseMap.All.Select(item => new { id = item.Id, name = item.Name, rows = item.Rows }).ToArray()
            };
        }

        private async Task LeaveCurrent()
        {
            if (!_world.Connections.TryRemove(Context.ConnectionId, out var seat)) return;
            if (_world.Rooms.TryGetValue(seat.RoomCode, out var room))
            {
                lock (room.Sync)
                {
                    room.RemovePlayer(seat.PlayerId);
                }
            }
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, seat.RoomCode);
        }

        private bool TryGetPlayer(out FuseRoom room, out FusePlayer player)
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
            if (user?.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(user.Identity.Name))
            {
                return Trim(user.Identity.Name);
            }
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
