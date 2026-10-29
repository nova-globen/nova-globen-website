using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Lek.Server.Noir
{
    /// <summary>The Bullet Noir tick: rooms step at a fixed 30 Hz and send 15 snapshots a second (three in the lobby).</summary>
    public sealed class NoirLoop : BackgroundService
    {
        private readonly NoirWorld _world;
        private readonly IServiceProvider _services;
        private readonly ILogger<NoirLoop> _logger;

        public NoirLoop(NoirWorld world, IServiceProvider services, ILogger<NoirLoop> logger)
        {
            _world = world;
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hub = _services.GetService<IHubContext<NoirHub>>();
            if (hub == null)
            {
                _logger.LogWarning("Bullet Noir: SignalR is disabled (Transport:SignalR:Enabled), the game loop is not running");
                return;
            }
            var clock = Stopwatch.StartNew();
            var simulated = 0.0;
            var sweep = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(1000.0 / 30));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = clock.Elapsed.TotalSeconds;
                var steps = 0;
                while (simulated + NoirPhysics.Dt <= now && steps < 3) { simulated += NoirPhysics.Dt; steps++; }
                if (now - simulated > 0.5) simulated = now;
                if (steps == 0) continue;
                foreach (var room in _world.Rooms.Values)
                {
                    object snapshot;
                    List<string> to = new();
                    lock (room.Sync)
                    {
                        if (room.Players.Count == 0) continue;
                        for (var i = 0; i < steps; i++) room.Step();
                        var every = room.Phase == NrPhase.Lobby ? 10 : 2;
                        if (room.Tick - room.LastSentTick < every && !(room.Phase == NrPhase.Lobby && room.Events.Count > 0)) continue;
                        room.LastSentTick = room.Tick;
                        snapshot = room.Snapshot();
                        room.Events.Clear();
                        foreach (var p in room.Players.Values) if (p.ConnectionId != null) to.Add(p.ConnectionId);
                    }
                    try
                    {
                        if (to.Count > 0) await hub.Clients.Clients(to).SendAsync("state", snapshot, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Bullet Noir: broadcast to room {Room} failed", room.Code); }
                }
                if (++sweep >= 30 * 30) { sweep = 0; _world.Sweep(TimeSpan.FromMinutes(10)); }
            }
        }
    }
}
