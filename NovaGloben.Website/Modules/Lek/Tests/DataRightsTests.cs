using Lek.Server.Controllers;
using Lek.Server.Services;
using Stomme.Erasure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;
using System.Text.Json;

namespace Lek.Tests;

/// <summary>A person deleted from the Lek site: out of Inside Job's list of players, and unnamed in other players' raid logs.</summary>
public sealed class DataRightsTests
{
    private const int SiteId = 3;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly MemorySettings _settings = new();

    private async Task SeedAsync()
    {
        await _settings.AddSettingAsync(new Setting { EntityName = EntityNames.Site, EntityId = SiteId, SettingName = InsideJobController.PlayersSetting,
            SettingValue = JsonSerializer.Serialize(new[] { new InsideJobController.PlayerRef { UserId = 5, Name = "five" }, new InsideJobController.PlayerRef { UserId = 6, Name = "six" } }, Json) });
        await _settings.AddSettingAsync(new Setting { EntityName = EntityNames.User, EntityId = 6, SettingName = InsideJobController.RaidsSetting,
            SettingValue = JsonSerializer.Serialize(new[] { new InsideJobController.RaidLog { Raider = "five", RaiderId = 5, Outcome = "clean" }, new InsideJobController.RaidLog { Raider = "seven", RaiderId = 7 } }, Json) });
    }

    [Fact]
    public async Task Deleting_a_person_removes_them_from_the_players_and_unnames_their_raids_on_others()
    {
        await SeedAsync();
        var erasure = new LekUserErasure(_settings);

        var outcomes = await erasure.EraseAsync(new ErasureSubject(SiteId, 5, null, "five"));

        outcomes.Select(o => (o.Store, o.Handling, o.Affected)).Should().Equal(
            (LekUserErasure.PlayersStore, ErasureHandling.Delete, 1),
            (LekUserErasure.RaidsStore, ErasureHandling.Anonymise, 1));
        var players = JsonSerializer.Deserialize<List<InsideJobController.PlayerRef>>((await _settings.GetSettingAsync(EntityNames.Site, SiteId, InsideJobController.PlayersSetting)).SettingValue, Json)!;
        players.Select(p => p.UserId).Should().Equal(6);
        var raids = JsonSerializer.Deserialize<List<InsideJobController.RaidLog>>((await _settings.GetSettingAsync(EntityNames.User, 6, InsideJobController.RaidsSetting)).SettingValue, Json)!;
        raids.Select(r => (r.Raider, r.RaiderId, r.Outcome)).Should().Equal((LekUserErasure.ErasedName, 0, "clean"), ("seven", 7, ""));

        (await erasure.EraseAsync(new ErasureSubject(SiteId, 5, null, "five"))).Sum(o => o.Affected).Should().Be(0, "a second run finds nothing");
    }

    private sealed class MemorySettings : ISettingRepository
    {
        private readonly List<Setting> _rows = new();
        private int _next = 1;

        public Task<IEnumerable<Setting>> GetSettingsAsync(string entityName) => Task.FromResult<IEnumerable<Setting>>(_rows.Where(s => s.EntityName == entityName).ToList());
        public Task<IEnumerable<Setting>> GetSettingsAsync(string entityName, int entityId) => Task.FromResult<IEnumerable<Setting>>(_rows.Where(s => s.EntityName == entityName && s.EntityId == entityId).ToList());
        public Task<IEnumerable<Setting>> GetSettingsAsync(string entityName1, int entityId1, string entityName2, int entityId2) => throw new NotSupportedException();
        public Task<Setting> AddSettingAsync(Setting setting) { setting.SettingId = _next++; _rows.Add(setting); return Task.FromResult(setting); }
        public Task<Setting> UpdateSettingAsync(Setting setting) => Task.FromResult(setting);
        public Task<Setting> GetSettingAsync(string entityName, int settingId) => Task.FromResult(_rows.FirstOrDefault(s => s.EntityName == entityName && s.SettingId == settingId)!);
        public Task<Setting> GetSettingAsync(string entityName, int entityId, string settingName) => Task.FromResult(_rows.FirstOrDefault(s => s.EntityName == entityName && s.EntityId == entityId && s.SettingName == settingName)!);
        public Task DeleteSettingAsync(string entityName, int settingId) { _rows.RemoveAll(s => s.EntityName == entityName && s.SettingId == settingId); return Task.CompletedTask; }
        public Task DeleteSettingsAsync(string entityName, int entityId) { _rows.RemoveAll(s => s.EntityName == entityName && s.EntityId == entityId); return Task.CompletedTask; }
        public Task<IEnumerable<string>> GetEntityNamesAsync() => Task.FromResult<IEnumerable<string>>(_rows.Select(s => s.EntityName).Distinct().ToList());
        public Task<IEnumerable<int>> GetEntityIdsAsync(string entityName) => Task.FromResult<IEnumerable<int>>(_rows.Where(s => s.EntityName == entityName).Select(s => s.EntityId).Distinct().ToList());
        public Task<string> GetSettingValueAsync(string entityName, int entityId, string settingName, string defaultValue) =>
            Task.FromResult(_rows.FirstOrDefault(s => s.EntityName == entityName && s.EntityId == entityId && s.SettingName == settingName)?.SettingValue ?? defaultValue);
    }
}