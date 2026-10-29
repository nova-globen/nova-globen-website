using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Lek.Server.RelicRush
{
    /// <summary>
    /// The Relic Rush tick: rooms step at a fixed 30 Hz and send 15 snapshots a second during a match (three in the
    /// lobby). Each team gets its own snapshot, because each team sees only its own shrine.
    /// </summary>
    public sealed class RelicLoop : BackgroundService
    {
        private readonly RelicWorld _world;
        private readonly IServiceProvider _services;
        private readonly ILogger<RelicLoop> _logger;

        public RelicLoop(RelicWorld world, IServiceProvider services, ILogger<RelicLoop> logger)
        {
            _world = world;
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hub = _services.GetService<IHubContext<RelicHub>>();
            if (hub == null)
            {
                _logger.LogWarning("Relic Rush: SignalR is disabled (Transport:SignalR:Enabled), the game loop is not running");
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
                while (simulated + RelicPhysics.Dt <= now && steps < 3) { simulated += RelicPhysics.Dt; steps++; }
                if (now - simulated > 0.5) simulated = now;
                if (steps == 0) continue;
                foreach (var room in _world.Rooms.Values)
                {
                    object team0, team1;
                    List<string> to0 = new(), to1 = new();
                    lock (room.Sync)
                    {
                        if (room.Players.Count == 0) continue;
                        for (var i = 0; i < steps; i++) room.Step();
                        var every = room.Phase == RrPhase.Lobby ? 10 : 2;
                        if (room.Tick - room.LastSentTick < every && !(room.Phase == RrPhase.Lobby && room.Events.Count > 0)) continue;
                        room.LastSentTick = room.Tick;
                        (team0, team1) = room.Snapshots();
                        room.Events.Clear();
                        foreach (var p in room.Players.Values)
                        {
                            if (p.ConnectionId == null) continue;
                            (p.Team == 1 ? to1 : to0).Add(p.ConnectionId);
                        }
                    }
                    try
                    {
                        if (to0.Count > 0) await hub.Clients.Clients(to0).SendAsync("state", team0, stoppingToken);
                        if (to1.Count > 0) await hub.Clients.Clients(to1).SendAsync("state", team1, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Relic Rush: broadcast to room {Room} failed", room.Code); }
                }
                if (++sweep >= 30 * 30) { sweep = 0; _world.Sweep(TimeSpan.FromMinutes(10)); }
            }
        }
    }
}
