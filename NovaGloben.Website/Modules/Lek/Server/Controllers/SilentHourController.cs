using System.Security.Claims;
using System.Text.Json;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Lek.Server.Services;

namespace Lek.Server.Controllers
{
    /// <summary>
    /// The signed-in user's Silent Hour progress: one result per raid (day number), kept as user settings in Stomme's
    /// own store (<c>silent-hour:day:{n}</c>), so no schema of our own is needed. A guest gets 401 and the game falls
    /// back to localStorage. The user is whoever the request's cookie authenticates as, so nobody can read or write
    /// another user's raids.
    /// </summary>
    /// <remarks>
    /// The site's global antiforgery filter is skipped for this controller because the game page calls it with plain
    /// fetch. In its place every write requires the <c>X-Requested-With: SilentHour</c> header, which a cross-site form
    /// cannot send and a cross-origin script cannot add without a CORS grant the site never gives.
    /// </remarks>
    [ApiController]
    [Route("api/silenthour")]
    [Authorize]
    [IgnoreAntiforgeryToken]
    public class SilentHourController : LekController
    {
        private const string Prefix = "silent-hour:day:";
        private const string Header = "X-Requested-With";
        private const string HeaderValue = "SilentHour";
        private static readonly DateTime Epoch = new(2026, 10, 11, 0, 0, 0, DateTimeKind.Utc);
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly ISettingRepository _settings;

        public SilentHourController(ISettingRepository settings)
        {
            _settings = settings;
        }

        public sealed class RaidResult
        {
            public int Turns { get; set; }
            public int Par { get; set; }
            public bool Takedown { get; set; }
            public int Alarms { get; set; }
            public int Hints { get; set; }
            public string? Date { get; set; } // yyyy-MM-dd, when it was first completed (server time)
        }

        // GET api/silenthour/progress -> { name, today, raids: { "12": {...} }, streak, perfect, played }
        [HttpGet("progress")]
        public async Task<IActionResult> Progress()
        {
            if (UserId() is not int userId) return Forbid();
            var raids = await LoadAsync(userId);
            return Ok(Summary(raids));
        }

        // PUT api/silenthour/day/{n}  { turns, par, takedown, alarms, hints }  -> the summary, after the raid is recorded
        // A later run of the same raid only replaces the stored result when it took fewer turns.
        [HttpPut("day/{n:int}")]
        public async Task<IActionResult> Record(int n, [FromBody] RaidResult result)
        {
            if (UserId() is not int userId) return Forbid();
            if (!Request.Headers.TryGetValue(Header, out var requested) || requested != HeaderValue) return BadRequest();
            if (n < 1 || n > 100000 || result == null || result.Turns < 1 || result.Turns > 500 || result.Par < 1 || result.Par > 500) return BadRequest();
            if (result.Alarms < 0 || result.Hints < 0 || result.Alarms > 10000 || result.Hints > 10000) return BadRequest();
            if (n > TodayNumber() + 1) return BadRequest(); // practice raids beyond tomorrow are not recorded

            var raids = await LoadAsync(userId);
            var name = Prefix + n;
            if (raids.TryGetValue(n, out var existing) && existing.Turns <= result.Turns)
            {
                return Ok(Summary(raids));
            }

            result.Date = existing?.Date ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
            var value = JsonSerializer.Serialize(result, Json);
            var setting = await _settings.GetSettingAsync(EntityNames.User, userId, name);
            if (setting == null)
            {
                await _settings.AddSettingAsync(new Setting { EntityName = EntityNames.User, EntityId = userId, SettingName = name, SettingValue = value, IsPrivate = true });
            }
            else
            {
                setting.SettingValue = value;
                await _settings.UpdateSettingAsync(setting);
            }
            raids[n] = result;
            return Ok(Summary(raids));
        }

        private async Task<Dictionary<int, RaidResult>> LoadAsync(int userId)
        {
            var raids = new Dictionary<int, RaidResult>();
            foreach (var setting in await _settings.GetSettingsAsync(EntityNames.User, userId))
            {
                if (!setting.SettingName.StartsWith(Prefix, StringComparison.Ordinal)) continue;
                if (!int.TryParse(setting.SettingName.AsSpan(Prefix.Length), out var day)) continue;
                try
                {
                    var result = JsonSerializer.Deserialize<RaidResult>(setting.SettingValue, Json);
                    if (result != null) raids[day] = result;
                }
                catch (JsonException)
                {
                    // a damaged row is skipped, never fatal
                }
            }
            return raids;
        }

        private object Summary(Dictionary<int, RaidResult> raids)
        {
            var today = TodayNumber();
            // the streak counts consecutive raids ending at the latest one played, as long as that one is recent: the
            // browser numbers days by its local date and this host by UTC, so "today" may differ by one either way
            var streak = 0;
            if (raids.Count > 0)
            {
                var day = raids.Keys.Max();
                if (day >= today - 1 && day <= today + 1)
                {
                    while (day >= 1 && raids.ContainsKey(day)) { streak++; day--; }
                }
            }
            return new
            {
                name = User.FindFirst(ClaimTypes.Name)?.Value ?? "",
                today,
                raids = raids.ToDictionary(item => item.Key.ToString(), item => item.Value),
                played = raids.Count,
                perfect = raids.Values.Count(item => item.Turns <= item.Par),
                streak
            };
        }

        private static int TodayNumber() => Math.Max(1, (int)Math.Round((DateTime.UtcNow.Date - Epoch).TotalDays) + 1);

        private int? UserId() => int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var id) ? id : null;
    }
}
