using System.Text.Json;
using Lek.Server.Controllers;
using Stomme.Erasure;
using Stomme.Models;
using Stomme.Repository;
using Stomme.Shared;

namespace Lek.Server.Services
{
    /// <summary>
    /// A person deleted from the Lek site: what Lek keeps under the person's own account (Inside Job's safehouse and
    /// raids, the game stats, Silent Hour) is a user setting, which Stomme's own erasure deletes. What it keeps elsewhere
    /// is handled here: their entry in Inside Job's site-wide list of players goes, and the raids they ran on other
    /// players' safehouses stay in those players' logs (they are the defenders' history) but no longer name them.
    /// Idempotent: a second run finds nothing to do.
    /// </summary>
    public sealed class LekUserErasure(ISettingRepository settings) : ISubjectErasureParticipant
    {
        public const string PlayersStore = "Lek Inside Job players";
        public const string RaidsStore = "Lek Inside Job raid logs";

        /// <summary>What another player's raid log names in place of the person.</summary>
        public const string ErasedName = "deleted account";

        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public string Name => "Lek";

        public IReadOnlyList<ErasureDeclaration> Declarations { get; } = new[]
        {
            ErasureDeclaration.Delete(PlayersStore) with { Summary = "your place in Lek's list of Inside Job players" },
            ErasureDeclaration.Anonymise(RaidsStore) with { Summary = "your name on the Lek raids you ran on other players (the raids stay)" },
        };

        public async Task<IReadOnlyList<ErasureOutcome>> EraseAsync(ErasureSubject subject, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(subject);
            var players = subject.SiteId > 0 && subject.UserId > 0
                ? await LoadAsync<List<InsideJobController.PlayerRef>>(EntityNames.Site, subject.SiteId, InsideJobController.PlayersSetting) ?? new()
                : new();

            // every other player's raid log: the person's raids on them stay, unnamed
            var raids = 0;
            foreach (var player in players.Where(p => p.UserId != subject.UserId))
            {
                var log = await LoadAsync<List<InsideJobController.RaidLog>>(EntityNames.User, player.UserId, InsideJobController.RaidsSetting);
                var mine = log?.Where(r => r.RaiderId == subject.UserId).ToList() ?? new();
                if (mine.Count == 0) continue;
                foreach (var raid in mine)
                {
                    raid.Raider = ErasedName;
                    raid.RaiderId = 0;
                }
                await SaveAsync(EntityNames.User, player.UserId, InsideJobController.RaidsSetting, log!);
                raids += mine.Count;
            }

            var removed = players.RemoveAll(p => p.UserId == subject.UserId);
            if (removed > 0)
            {
                await SaveAsync(EntityNames.Site, subject.SiteId, InsideJobController.PlayersSetting, players);
            }

            return new[]
            {
                new ErasureOutcome(PlayersStore, ErasureHandling.Delete, removed),
                new ErasureOutcome(RaidsStore, ErasureHandling.Anonymise, raids),
            };
        }

        private async Task<T?> LoadAsync<T>(string entity, int id, string name) where T : class
        {
            var setting = await settings.GetSettingAsync(entity, id, name);
            if (setting == null || string.IsNullOrEmpty(setting.SettingValue)) return null;
            try { return JsonSerializer.Deserialize<T>(setting.SettingValue, Json); }
            catch (JsonException) { return null; }
        }

        private async Task SaveAsync<T>(string entity, int id, string name, T value)
        {
            var setting = await settings.GetSettingAsync(entity, id, name);
            if (setting == null) return;
            setting.SettingValue = JsonSerializer.Serialize(value, Json);
            await settings.UpdateSettingAsync(setting);
        }
    }
}