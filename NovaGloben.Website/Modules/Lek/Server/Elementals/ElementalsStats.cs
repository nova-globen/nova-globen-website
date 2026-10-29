using System.Text.Json;
using Stomme.Infrastructure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace Lek.Server.Elementals
{
    /// <summary>
    /// A signed-in player's Elementals record across all rooms: matches played and won, kept as one Stomme user setting
    /// (<c>elementals:stats</c>). Read when the player joins a room, written by the loop when a match ends. Stomme's
    /// repositories are scoped and bound to the request's tenant, so every call here opens its own scope and points
    /// it at the player's tenant and site first (the loop has no request to inherit them from).
    /// </summary>
    public sealed class ElementalsStats
    {
        public const string SettingName = "elementals:stats";
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
        private readonly IServiceScopeFactory _scopes;

        public sealed class Record
        {
            public int Wins { get; set; }
            public int Matches { get; set; }
        }

        public ElementalsStats(IServiceScopeFactory scopes)
        {
            _scopes = scopes;
        }

        public async Task<Record> LoadAsync(int tenantId, int siteId, int userId)
        {
            using var scope = _scopes.CreateScope();
            var settings = await BindAsync(scope, tenantId, siteId);
            var setting = await settings.GetSettingAsync(EntityNames.User, userId, SettingName);
            return Parse(setting);
        }

        public async Task RecordAsync(IEnumerable<ElMatchResult> results)
        {
            foreach (var site in results.GroupBy(item => (item.TenantId, item.SiteId)))
            {
                using var scope = _scopes.CreateScope();
                var settings = await BindAsync(scope, site.Key.TenantId, site.Key.SiteId);
                foreach (var group in site.GroupBy(item => item.UserId))
                {
                    var setting = await settings.GetSettingAsync(EntityNames.User, group.Key, SettingName);
                    var record = Parse(setting);
                    record.Matches += group.Count();
                    record.Wins += group.Count(item => item.Won);
                    var value = JsonSerializer.Serialize(record, Json);
                    if (setting == null)
                    {
                        await settings.AddSettingAsync(new Setting { EntityName = EntityNames.User, EntityId = group.Key, SettingName = SettingName, SettingValue = value, IsPrivate = true });
                    }
                    else
                    {
                        setting.SettingValue = value;
                        await settings.UpdateSettingAsync(setting);
                    }
                }
            }
        }

        private static async Task<ISettingRepository> BindAsync(IServiceScope scope, int tenantId, int siteId)
        {
            await scope.ServiceProvider.GetRequiredService<ITenantManager>().SetAliasAsync(tenantId, siteId);
            return scope.ServiceProvider.GetRequiredService<ISettingRepository>();
        }

        private static Record Parse(Setting? setting)
        {
            if (setting == null || string.IsNullOrEmpty(setting.SettingValue)) return new Record();
            try { return JsonSerializer.Deserialize<Record>(setting.SettingValue, Json) ?? new Record(); }
            catch (JsonException) { return new Record(); }
        }
    }
}
