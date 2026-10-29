using Stomme.Extensions;
using Microsoft.AspNetCore.SignalR;

using Lek.Server.Services;

namespace Lek.Server.Elementals
{
    /// <summary>
    /// The Elementals hub: membership, lobby choices, inputs and presses in; the loop broadcasts "state" to the
    /// room's group 30 times a second. Anonymous on purpose: guests play under a nickname, signed-in users under their
    /// display name, and a signed-in user's match wins are kept on the account (<see cref="ElementalsStats"/>).
    /// </summary>
    public sealed class ElementalsHub : LekHub
    {
        private readonly ElementalsWorld _world;
        private readonly ElementalsStats _stats;

        public ElementalsHub(ElementalsWorld world, ElementalsStats stats)
        {
            _world = world;
            _stats = stats;
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

        /// <summary>One input per client tick (30 Hz): sequence number, held buttons, aim in radians.</summary>
        public void Input(int seq, int bits, double aim)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) room.QueueInput(player, seq, bits, double.IsFinite(aim) ? aim : double.NaN);
        }

        public void Ability(int slot)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) player.WantAbility = slot;
        }

        public void Swap(int weapon)
        {
            if (!TryGetPlayer(out var room, out var player)) return;
            lock (room.Sync) player.WantWeapon = weapon == 1 ? 1 : 0;
        }

        public object SetKind(string kind) => Owned(false, room => room.SetKind(PlayerId()!, kind));
        public object SetArms(string[] ids) => Owned(false, room => room.SetArms(PlayerId()!, ids));
        public object SetLoadout(string[] ids) => Owned(false, room => room.SetLoadout(PlayerId()!, ids));
        public object SetTeam(int team) => Owned(false, room => room.SetTeam(PlayerId()!, team));
        public object SetMode(int size) => Owned(true, room => room.SetMode(size));
        public object SetMap(string id) => Owned(true, room => room.SetMap(id));
        public object Start() => Owned(true, room => room.Start());

        public double Ping(double stamp) => stamp;

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await LeaveCurrent();
            await base.OnDisconnectedAsync(exception);
        }

        private object Owned(bool ownerOnly, Func<ElementalsRoom, string?> action)
        {
            if (!TryGetPlayer(out var room, out var player)) return new { error = "You are not in a room." };
            lock (room.Sync)
            {
                if (ownerOnly && room.OwnerId != player.Id) return new { error = "Only the room owner can do that." };
                var error = action(room);
                return error == null ? new { ok = true } : new { error };
            }
        }

        private string? PlayerId() => _world.Connections.TryGetValue(Context.ConnectionId, out var seat) ? seat.PlayerId : null;

        private async Task<object> Enter(ElementalsRoom room, string? nickname)
        {
            var name = ResolveName(nickname);
            var id = Guid.NewGuid().ToString("N")[..8];
            var userId = int.TryParse(Context.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var uid) ? uid : (int?)null;
            var tenantId = 0;
            var siteId = 0;
            ElementalsStats.Record? stats = null;
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
                if (room.HumanCount >= ElementalsRoom.MaxPlayers) return new { error = "The room is full (6 players)." };
                var player = room.AddPlayer(id, name, false);
                player.UserId = userId;
                player.TenantId = tenantId;
                player.SiteId = siteId;
                if (stats != null) player.TotalWins = stats.Wins;
            }
            _world.Connections[Context.ConnectionId] = (room.Code, id);
            await Groups.AddToGroupAsync(Context.ConnectionId, room.Code);
            return new
            {
                code = room.Code,
                playerId = id,
                name,
                maps = ElementalsMap.All.Select(m => new { id = m.Id, name = m.Name, theme = m.Theme, rows = m.Rows }).ToArray(),
                common = KindDef.Common.Select(w => new { id = w.Id, name = w.Name, melee = w.Melee, damage = w.Damage, cooldown = w.Cooldown, note = w.Note }).ToArray(),
                kinds = KindDef.All.Select(k => new
                {
                    id = k.Id, name = k.Name, element = k.Element, hearts = k.Hearts, w = k.W, h = k.H, tradeoff = k.Tradeoff,
                    weapons = k.Weapons.Select(w => new { id = w.Id, name = w.Name, melee = w.Melee, damage = w.Damage, cooldown = w.Cooldown, note = w.Note }).ToArray(),
                    pool = k.Pool.Select(a => new { id = a.Id, name = a.Name, cooldown = a.Cooldown, note = a.Note, passive = a.Passive }).ToArray(),
                    loadout = k.DefaultLoadout,
                    arms = k.DefaultArms
                }).ToArray()
            };
        }

        private async Task LeaveCurrent()
        {
            if (!_world.Connections.TryRemove(Context.ConnectionId, out var seat)) return;
            if (_world.Rooms.TryGetValue(seat.RoomCode, out var room))
            {
                lock (room.Sync) room.RemovePlayer(seat.PlayerId);
            }
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, seat.RoomCode);
        }

        private bool TryGetPlayer(out ElementalsRoom room, out ElPlayer player)
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
