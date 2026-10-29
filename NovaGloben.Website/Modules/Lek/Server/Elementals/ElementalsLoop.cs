using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Lek.Server.Elementals
{
    /// <summary>
    /// The Elementals tick: every room steps at a fixed 30 Hz (catching up when the timer is late, at most three steps
    /// a beat) and its snapshot goes to its group after each beat. Match results are written to accounts here.
    /// </summary>
    public sealed class ElementalsLoop : BackgroundService
    {
        private readonly ElementalsWorld _world;
        private readonly ElementalsStats _stats;
        private readonly IServiceProvider _services;
        private readonly ILogger<ElementalsLoop> _logger;

        public ElementalsLoop(ElementalsWorld world, ElementalsStats stats, IServiceProvider services, ILogger<ElementalsLoop> logger)
        {
            _world = world;
            _stats = stats;
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hub = _services.GetService<IHubContext<ElementalsHub>>();
            if (hub == null)
            {
                _logger.LogWarning("Elementals: SignalR is disabled (Transport:SignalR:Enabled), the game loop is not running");
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
                while (simulated + ElementalsPhysics.Dt <= now && steps < 3) { simulated += ElementalsPhysics.Dt; steps++; }
                if (now - simulated > 0.5) simulated = now; // fell far behind: drop the backlog
                if (steps == 0) continue;

                foreach (var room in _world.Rooms.Values)
                {
                    object snapshot;
                    List<ElMatchResult>? results = null;
                    lock (room.Sync)
                    {
                        if (room.Players.Count == 0) continue;
                        for (var i = 0; i < steps; i++) room.Step();
                        // the lobby changes slowly: three snapshots a second are plenty; a match sends 15 a second (the
                        // client predicts its own hero and interpolates the rest), which halves the traffic on slow links
                        var every = room.Phase == ElPhase.Lobby ? 10 : 2;
                        if (room.Tick - room.LastSentTick < every && !(room.Phase == ElPhase.Lobby && room.Events.Count > 0)) continue;
                        room.LastSentTick = room.Tick;
                        snapshot = room.Snapshot();
                        room.Events.Clear();
                        if (room.Results.Count > 0) { results = room.Results.ToList(); room.Results.Clear(); }
                    }
                    if (results != null)
                    {
                        try { await _stats.RecordAsync(results); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Elementals: recording match results failed"); }
                    }
                    try { await hub.Clients.Group(room.Code).SendAsync("state", snapshot, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Elementals: broadcast to room {Room} failed", room.Code); }
                }
                if (++sweep >= 30 * 30) { sweep = 0; _world.Sweep(TimeSpan.FromMinutes(10)); }
            }
        }
    }
}
