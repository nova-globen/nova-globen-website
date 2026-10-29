using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Lek.Server.Fuse
{
    /// <summary>
    /// The server tick: 20 times a second every room advances and its snapshot goes to its SignalR group. Runs only
    /// when SignalR is enabled (otherwise the hub is not mapped and there is nobody to send to).
    /// </summary>
    public sealed class FuseLoop : BackgroundService
    {
        private const int TickMs = 50;
        private readonly FuseWorld _world;
        private readonly FuseStats _stats;
        private readonly IServiceProvider _services;
        private readonly ILogger<FuseLoop> _logger;

        public FuseLoop(FuseWorld world, FuseStats stats, IServiceProvider services, ILogger<FuseLoop> logger)
        {
            _world = world;
            _stats = stats;
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hub = _services.GetService<IHubContext<FuseHub>>();
            if (hub == null)
            {
                _logger.LogWarning("Fuse: SignalR is disabled (Transport:SignalR:Enabled), the game loop is not running");
                return;
            }

            var clock = Stopwatch.StartNew();
            var last = clock.Elapsed.TotalSeconds;
            var sweep = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(TickMs));
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                var now = clock.Elapsed.TotalSeconds;
                var dt = (float)Math.Min(now - last, 0.1);
                last = now;

                foreach (var room in _world.Rooms.Values)
                {
                    object snapshot;
                    List<FuseMatchResult>? results = null;
                    lock (room.Sync)
                    {
                        if (room.Players.Count == 0) continue;
                        room.Tick(now, dt);
                        snapshot = room.Snapshot();
                        room.Events.Clear();
                        if (room.Results.Count > 0) { results = room.Results.ToList(); room.Results.Clear(); }
                    }
                    if (results != null)
                    {
                        try { await _stats.RecordAsync(results); }
                        catch (Exception ex) { _logger.LogWarning(ex, "Fuse: recording match results failed"); }
                    }
                    try
                    {
                        await hub.Clients.Group(room.Code).SendAsync("state", snapshot, stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        _logger.LogWarning(ex, "Fuse: broadcast to room {Room} failed", room.Code);
                    }
                }

                if (++sweep >= 20 * 30)
                {
                    sweep = 0;
                    _world.Sweep(TimeSpan.FromMinutes(10));
                }
            }
        }
    }
}
