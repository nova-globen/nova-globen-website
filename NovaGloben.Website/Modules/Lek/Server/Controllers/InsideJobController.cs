using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Lek.Server.Services;

namespace Lek.Server.Controllers
{
    /// <summary>
    /// Inside Job's account side. Each player has a safehouse (a layout the browser's solver has rated), a vault of
    /// loot that fills over time, and a log of the raids run against them. Raids are solo and asynchronous: the
    /// raider plays against a copy of the layout and posts the action list; the server replays nothing yet (the rules
    /// live in JavaScript), so it trusts the claimed turn count within sanity limits. A C# port of the rules for
    /// server-side replay is the planned hardening.
    /// </summary>
    [ApiController]
    [Route("api/insidejob")]
    [Authorize]
    [IgnoreAntiforgeryToken]
    public class InsideJobController : LekController
    {
        private const string BaseSetting = "insidejob:base";
        private const string RaidsSetting = "insidejob:raids";
        private const string PlayersSetting = "insidejob:players";
        private const string Header = "X-Requested-With";
        private const string HeaderValue = "InsideJob";
        private const int LootPerHour = 100;
        private const int LootCap = 2000;
        private const int LootStart = 500;
        private const int RaidLogSize = 10;
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ISettingRepository _settings;

        public InsideJobController(ISettingRepository settings)
        {
            _settings = settings;
        }

        public sealed class Safehouse
        {
            public JsonObject? Layout { get; set; }
            public int Par { get; set; }
            public int[]? Solution { get; set; }
            public int Loot { get; set; } = LootStart;
            public DateTime LootAt { get; set; } = DateTime.UtcNow;
            public int Stolen { get; set; }        // taken from me, all time
            public int Earned { get; set; }        // taken by me, all time
            public int RaidsRun { get; set; }
            public int RaidsSuffered { get; set; }
            public int Defended { get; set; }      // raiders caught in my safehouse
            public string? Name { get; set; }
            public DateTime? SavedAt { get; set; }
        }

        public sealed class RaidLog
        {
            public string Raider { get; set; } = "";
            public int RaiderId { get; set; }
            public DateTime When { get; set; }
            public int[] Acts { get; set; } = Array.Empty<int>();
            public int Turns { get; set; }
            public string Outcome { get; set; } = "";   // perfect, clean, slow, caught
            public int Loot { get; set; }              // what changed hands, positive = stolen from the defender
            public int Par { get; set; }
            public JsonObject? Layout { get; set; }     // the safehouse as it was, so the replay stays true after a redesign
        }

        public sealed class PlayerRef
        {
            public int UserId { get; set; }
            public string Name { get; set; } = "";
        }

        public sealed class BaseUpdate
        {
            public JsonObject? Layout { get; set; }
            public int Par { get; set; }
            public int[]? Solution { get; set; }
        }

        public sealed class RaidReport
        {
            public int TargetId { get; set; }
            public int[]? Acts { get; set; }
            public bool Caught { get; set; }
            public int Alarms { get; set; }
        }

        // GET api/insidejob/me -> my safehouse, vault, and the raids against me
        [HttpGet("me")]
        public async Task<IActionResult> Me()
        {
            if (UserId() is not int userId) return Forbid();
            var mine = await LoadBaseAsync(userId);
            Accrue(mine);
            var raids = await LoadRaidsAsync(userId);
            return Ok(new { name = Name(), userId, safehouse = mine, raids });
        }

        // PUT api/insidejob/base { layout, par, solution } -> saves my safehouse (the browser has rated it)
        [HttpPut("base")]
        public async Task<IActionResult> SaveBase([FromBody] BaseUpdate update)
        {
            if (UserId() is not int userId) return Forbid();
            if (!HasHeader()) return BadRequest();
            if (update?.Layout == null || update.Par < 1 || update.Par > 200 || update.Solution == null || update.Solution.Length != update.Par) return BadRequest();
            if (update.Layout["wall"] is not JsonArray wall || wall.Count != 81) return BadRequest();
            if (update.Layout["guards"] is not JsonArray guards || guards.Count != 3) return BadRequest();
            var mine = await LoadBaseAsync(userId);
            Accrue(mine);
            mine.Layout = update.Layout;
            mine.Par = update.Par;
            mine.Solution = update.Solution;
            mine.Name = Name();
            mine.SavedAt = DateTime.UtcNow;
            await SaveBaseAsync(userId, mine);
            await RegisterAsync(userId, Name());
            return Ok(new { safehouse = mine });
        }

        // GET api/insidejob/targets -> everyone else with a rated safehouse
        [HttpGet("targets")]
        public async Task<IActionResult> Targets()
        {
            if (UserId() is not int userId) return Forbid();
            var players = await LoadPlayersAsync();
            var list = new List<object>();
            foreach (var p in players.Where(item => item.UserId != userId))
            {
                var b = await LoadBaseAsync(p.UserId);
                if (b.Layout == null) continue;
                Accrue(b);
                list.Add(new { userId = p.UserId, name = p.Name, par = b.Par, loot = b.Loot, raidsSuffered = b.RaidsSuffered, defended = b.Defended });
            }
            return Ok(new { targets = list.OrderByDescending(item => ((dynamic)item).loot).ToList() });
        }

        // GET api/insidejob/base/{userId} -> a target's layout (no solution: that is the defender's secret)
        [HttpGet("base/{targetId:int}")]
        public async Task<IActionResult> Target(int targetId)
        {
            if (UserId() is not int userId) return Forbid();
            var b = await LoadBaseAsync(targetId);
            if (b.Layout == null) return NotFound();
            Accrue(b);
            return Ok(new { userId = targetId, name = b.Name, layout = b.Layout, par = b.Par, loot = b.Loot });
        }

        // POST api/insidejob/raid { targetId, acts, caught, alarms } -> settles the loot and logs the raid for the defender
        [HttpPost("raid")]
        public async Task<IActionResult> Raid([FromBody] RaidReport report)
        {
            if (UserId() is not int userId) return Forbid();
            if (!HasHeader()) return BadRequest();
            if (report == null || report.TargetId == userId || report.Acts == null || report.Acts.Length > 400 || report.Acts.Any(a => a < 0 || a > 11)) return BadRequest();
            var target = await LoadBaseAsync(report.TargetId);
            if (target.Layout == null) return NotFound();
            var mine = await LoadBaseAsync(userId);
            Accrue(target);
            Accrue(mine);

            var turns = report.Acts.Length;
            string outcome; int loot;
            if (report.Caught)
            {
                outcome = "caught";
                loot = -Math.Min(mine.Loot, Math.Max(25, mine.Loot / 10));
            }
            else
            {
                var share = turns <= target.Par ? 0.30 : turns <= target.Par + 3 ? 0.15 : 0.05;
                outcome = turns <= target.Par ? "perfect" : turns <= target.Par + 3 ? "clean" : "slow";
                loot = Math.Min(target.Loot, Math.Max(target.Loot > 0 ? 25 : 0, (int)(target.Loot * share)));
            }
            target.Loot -= loot;
            mine.Loot += loot;
            if (loot > 0) { target.Stolen += loot; mine.Earned += loot; }
            if (report.Caught) target.Defended++;
            target.RaidsSuffered++;
            mine.RaidsRun++;
            await SaveBaseAsync(report.TargetId, target);
            await SaveBaseAsync(userId, mine);

            var raids = await LoadRaidsAsync(report.TargetId);
            raids.Insert(0, new RaidLog { Raider = Name(), RaiderId = userId, When = DateTime.UtcNow, Acts = report.Acts, Turns = turns, Outcome = outcome, Loot = loot, Par = target.Par, Layout = target.Layout });
            if (raids.Count > RaidLogSize) raids.RemoveRange(RaidLogSize, raids.Count - RaidLogSize);
            await SaveRaidsAsync(report.TargetId, raids);

            return Ok(new { outcome, loot, turns, par = target.Par, myLoot = mine.Loot });
        }

        private static void Accrue(Safehouse b)
        {
            if (b.Layout == null) return;
            var hours = (DateTime.UtcNow - b.LootAt).TotalHours;
            if (hours <= 0) return;
            var gain = (int)(hours * LootPerHour);
            if (gain <= 0) return;
            b.Loot = Math.Min(LootCap, b.Loot + gain);
            b.LootAt = DateTime.UtcNow;
        }

        private bool HasHeader() => Request.Headers.TryGetValue(Header, out var requested) && requested == HeaderValue;
        private string Name() => User.FindFirst(ClaimTypes.Name)?.Value ?? "player";
        private int? UserId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
        private int SiteId() => int.TryParse(User.FindFirst("sitekey")?.Value?.Split(':').LastOrDefault(), out var id) ? id : 1;

        private async Task<Safehouse> LoadBaseAsync(int userId) => await LoadAsync<Safehouse>(EntityNames.User, userId, BaseSetting) ?? new Safehouse();
        private Task SaveBaseAsync(int userId, Safehouse b) => SaveAsync(EntityNames.User, userId, BaseSetting, b);
        private async Task<List<RaidLog>> LoadRaidsAsync(int userId) => await LoadAsync<List<RaidLog>>(EntityNames.User, userId, RaidsSetting) ?? new List<RaidLog>();
        private Task SaveRaidsAsync(int userId, List<RaidLog> raids) => SaveAsync(EntityNames.User, userId, RaidsSetting, raids);
        private async Task<List<PlayerRef>> LoadPlayersAsync() => await LoadAsync<List<PlayerRef>>(EntityNames.Site, SiteId(), PlayersSetting) ?? new List<PlayerRef>();

        private async Task RegisterAsync(int userId, string name)
        {
            var players = await LoadPlayersAsync();
            var me = players.FirstOrDefault(item => item.UserId == userId);
            if (me == null) players.Add(new PlayerRef { UserId = userId, Name = name });
            else if (me.Name == name) return;
            else me.Name = name;
            await SaveAsync(EntityNames.Site, SiteId(), PlayersSetting, players);
        }

        private async Task<T?> LoadAsync<T>(string entity, int id, string name) where T : class
        {
            var setting = await _settings.GetSettingAsync(entity, id, name);
            if (setting == null || string.IsNullOrEmpty(setting.SettingValue)) return null;
            try { return JsonSerializer.Deserialize<T>(setting.SettingValue, Json); }
            catch (JsonException) { return null; }
        }

        private async Task SaveAsync<T>(string entity, int id, string name, T value)
        {
            var json = JsonSerializer.Serialize(value, Json);
            var setting = await _settings.GetSettingAsync(entity, id, name);
            if (setting == null)
            {
                await _settings.AddSettingAsync(new Setting { EntityName = entity, EntityId = id, SettingName = name, SettingValue = json, IsPrivate = true });
            }
            else
            {
                setting.SettingValue = json;
                await _settings.UpdateSettingAsync(setting);
            }
        }
    }
}
