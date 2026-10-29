using System.Diagnostics;
using Microsoft.AspNetCore.SignalR;

namespace Lek.Server.Spark
{
    /// <summary>The Spark server tick: 20 times a second every room advances and its snapshot goes to its group.</summary>
    public sealed class SparkLoop : BackgroundService
    {
        private const int TickMs = 50;
        private readonly SparkWorld _world;
        private readonly IServiceProvider _services;
        private readonly ILogger<SparkLoop> _logger;

        public SparkLoop(SparkWorld world, IServiceProvider services, ILogger<SparkLoop> logger)
        {
            _world = world;
            _services = services;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var hub = _services.GetService<IHubContext<SparkHub>>();
            if (hub == null)
            {
                _logger.LogWarning("Spark: SignalR is disabled (Transport:SignalR:Enabled), the game loop is not running");
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
                    lock (room.Sync)
                    {
                        if (room.Players.Count == 0) continue;
                        room.Tick(now, dt);
                        snapshot = room.Snapshot();
                        room.Events.Clear();
                    }
                    try { await hub.Clients.Group(room.Code).SendAsync("state", snapshot, stoppingToken); }
                    catch (Exception ex) when (ex is not OperationCanceledException) { _logger.LogWarning(ex, "Spark: broadcast to room {Room} failed", room.Code); }
                }
                if (++sweep >= 20 * 30) { sweep = 0; _world.Sweep(TimeSpan.FromMinutes(10)); }
            }
        }
    }
}
