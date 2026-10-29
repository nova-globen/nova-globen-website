using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Server.Services;

/// <summary>
/// Brings a whole earlier ledger onto the Delato site, once: its groups (with members and couples/subgroups),
/// expenses and settlements, by their own ids.
/// <list type="bullet">
/// <item><b>All or nothing:</b> the WHOLE payload is validated before anything is stored. The first bad row is named by
/// table, index, id and column (<c>expenses[3] (id 17): amountUsd ...</c>), never by its values, and nothing is
/// written.</item>
/// <item><b>Create if absent:</b> a record already on the site is skipped and reported, never overwritten, so a second
/// import neither undoes what members changed since nor writes anything when nothing is new.</item>
/// <item><b>Never onto the site's own records:</b> an id used by a group the site made itself (or by its members,
/// couples/subgroups, expenses or settlements) refuses the whole payload with 409.</item>
/// <item><b>Ids:</b> the site's id counters are raised past every imported id, so the next new record follows them.</item>
/// <item><b>Accounts are not imported.</b> A member keeps its e-mail and is claimed by the account with that e-mail,
/// confirmed. A group's creator becomes the account that claims the member its creator was; until then its
/// <see cref="Group.CreatedByUserId"/> is 0, which matches no caller.</item>
/// <item>Records keep their own creation times; the shares are kept as they were computed, after checking that they
/// sum to the expense's USD amount.</item>
/// </list>
/// </summary>
public sealed class DelatoImport(DelatoStore store)
{
    public async Task<Outcome> ImportAsync(int siteId, ImportRequest? request, CancellationToken ct = default)
    {
        var groups = request?.Groups ?? [];
        var expenses = request?.Expenses ?? [];
        var settlements = request?.Settlements ?? [];
        if (groups.Count + expenses.Count + settlements.Count == 0)
        {
            return Outcome.Validation("The payload holds nothing to import.");
        }

        await DelatoWrites.Gate.WaitAsync(ct);
        try
        {
            var existingGroups = (await store.AllAsync<Group>(siteId, DelatoKind.Group, ct)).ToDictionary(g => g.Id);
            if (Validate(groups, expenses, settlements, existingGroups) is { } error)
            {
                return Outcome.Validation(error);
            }

            var existingExpenses = await store.AllAsync<Expense>(siteId, DelatoKind.Expense, ct);
            var existingSettlements = await store.AllAsync<Settlement>(siteId, DelatoKind.Settlement, ct);
            if (Conflict(groups, expenses, settlements, existingGroups, existingExpenses, existingSettlements) is { } conflict)
            {
                return Outcome.Conflict("CONFLICT", conflict);
            }

            var created = 0;
            var skipped = new List<string>();
            foreach (var g in groups)
            {
                if (existingGroups.ContainsKey(g.Id)) { skipped.Add($"group {g.Id}"); continue; }
                await store.StoreAsync(siteId, DelatoKind.Group, g.Id, 0, ToGroup(g), ct);
                created++;
            }
            var expenseIds = existingExpenses.Select(e => e.Id).ToHashSet();
            foreach (var e in expenses)
            {
                if (expenseIds.Contains(e.Id)) { skipped.Add($"expense {e.Id}"); continue; }
                await store.StoreAsync(siteId, DelatoKind.Expense, e.GroupId, e.Id, ToExpense(e), ct);
                created++;
            }
            var settlementIds = existingSettlements.Select(s => s.Id).ToHashSet();
            foreach (var s in settlements)
            {
                if (settlementIds.Contains(s.Id)) { skipped.Add($"settlement {s.Id}"); continue; }
                await store.StoreAsync(siteId, DelatoKind.Settlement, s.GroupId, s.Id, ToSettlement(s), ct);
                created++;
            }

            var ids = await store.LoadAsync<IdCounters>(siteId, DelatoKind.Ids, 0, 0, ct) ?? new IdCounters();
            ids.Group = Math.Max(ids.Group, groups.Select(g => g.Id).DefaultIfEmpty().Max());
            ids.Member = Math.Max(ids.Member, groups.SelectMany(g => g.Members ?? []).Select(m => m.Id).DefaultIfEmpty().Max());
            ids.Entity = Math.Max(ids.Entity, groups.SelectMany(g => g.Entities ?? []).Select(e => e.Id).DefaultIfEmpty().Max());
            ids.Expense = Math.Max(ids.Expense, expenses.Select(e => e.Id).DefaultIfEmpty().Max());
            ids.Settlement = Math.Max(ids.Settlement, settlements.Select(s => s.Id).DefaultIfEmpty().Max());
            await store.StoreAsync(siteId, DelatoKind.Ids, 0, 0, ids, ct);

            return Outcome.Ok(new ImportResult(
                groups.Count,
                groups.Sum(g => g.Members?.Count ?? 0),
                groups.Sum(g => g.Entities?.Count ?? 0),
                expenses.Count,
                settlements.Count,
                created,
                skipped));
        }
        finally
        {
            DelatoWrites.Gate.Release();
        }
    }

    /// <summary>An enum value by its exact name only: no other case, no number, no whitespace.</summary>
    internal static bool Exact<T>(string? text, out T value) where T : struct, Enum
    {
        value = default;
        return text is not null && Enum.GetNames<T>().Contains(text, StringComparer.Ordinal) && Enum.TryParse(text, out value);
    }

    // ---------------------------------------------------------------- validation (all of it, before any write)

    /// <summary>
    /// The first thing wrong with the payload, named by table, index, id and column - never by a value - or null. A
    /// group already on the site is checked against itself: its expenses and settlements must fit it.
    /// </summary>
    internal static string? Validate(List<ImportGroup> groups, List<ImportExpense> expenses, List<ImportSettlement> settlements,
        IReadOnlyDictionary<int, Group>? onSite = null)
    {
        var groupIds = new HashSet<int>();
        var memberIds = new HashSet<int>();
        var entityIds = new HashSet<int>();
        var membersOf = new Dictionary<int, HashSet<int>>();
        var entitiesOf = new Dictionary<int, HashSet<int>>();

        for (var i = 0; i < groups.Count; i++)
        {
            var g = groups[i];
            if (g is null) return $"groups[{i}]: the row is missing.";
            string Bad(string why) => $"groups[{i}] (id {g.Id}): {why}";
            if (g.Id <= 0) return Bad("id must be positive.");
            if (!groupIds.Add(g.Id)) return Bad("id is used twice.");
            if (string.IsNullOrWhiteSpace(g.Name)) return Bad("name is required.");
            if (!Currencies.IsValid(g.BaseCurrency)) return Bad("baseCurrency is not a known currency.");
            if (g.CreatedAt == default) return Bad("createdAt is missing.");

            var members = new HashSet<int>();
            var emails = new HashSet<string>();
            var list = g.Members ?? [];
            for (var j = 0; j < list.Count; j++)
            {
                var m = list[j];
                if (m is null) return Bad($"members[{j}]: the row is missing.");
                string BadMember(string why) => Bad($"members[{j}] (id {m.Id}): {why}");
                if (m.Id <= 0) return BadMember("id must be positive.");
                if (!memberIds.Add(m.Id)) return BadMember("id is used twice.");
                if (string.IsNullOrWhiteSpace(m.Name)) return BadMember("name is required.");
                if (DelatoCaller.NormalizeEmail(m.Email) is { } email && !emails.Add(email)) return BadMember("email is used twice in the group.");
                members.Add(m.Id);
            }
            if (g.CreatorMemberId is { } creator && !members.Contains(creator)) return Bad("creatorMemberId is not a member of the group.");

            var entities = new HashSet<int>();
            var inCouple = new HashSet<int>();
            var entityList = g.Entities ?? [];
            for (var j = 0; j < entityList.Count; j++)
            {
                var e = entityList[j];
                if (e is null) return Bad($"entities[{j}]: the row is missing.");
                string BadEntity(string why) => Bad($"entities[{j}] (id {e.Id}): {why}");
                if (e.Id <= 0) return BadEntity("id must be positive.");
                if (!entityIds.Add(e.Id)) return BadEntity("id is used twice.");
                if (string.IsNullOrWhiteSpace(e.Name)) return BadEntity("name is required.");
                if (!Exact<EntityKind>(e.Kind, out var kind)) return BadEntity("kind is not Couple or Subgroup.");
                var ids = (e.MemberIds ?? []).Distinct().ToList();
                if (ids.Any(id => !members.Contains(id))) return BadEntity("memberIds holds a member not in the group.");
                if (kind == EntityKind.Couple && ids.Count != 2) return BadEntity("memberIds: a couple has exactly 2 members.");
                if (kind == EntityKind.Subgroup && ids.Count < 2) return BadEntity("memberIds: a subgroup has at least 2 members.");
                if (kind == EntityKind.Couple && ids.Any(id => !inCouple.Add(id))) return BadEntity("memberIds: a member is in two couples.");
                entities.Add(e.Id);
            }
            membersOf[g.Id] = members;
            entitiesOf[g.Id] = entities;
        }

        // an expense or settlement may also belong to a group the site already has from an earlier import
        HashSet<int>? MembersOf(int groupId) =>
            membersOf.TryGetValue(groupId, out var m) ? m
            : onSite is not null && onSite.TryGetValue(groupId, out var g) && g.Imported ? g.Members.Select(x => x.Id).ToHashSet() : null;
        HashSet<int> EntitiesOf(int groupId) =>
            entitiesOf.TryGetValue(groupId, out var e) ? e : onSite![groupId].Entities.Select(x => x.Id).ToHashSet();

        var expenseIds = new HashSet<int>();
        for (var i = 0; i < expenses.Count; i++)
        {
            var e = expenses[i];
            if (e is null) return $"expenses[{i}]: the row is missing.";
            string Bad(string why) => $"expenses[{i}] (id {e.Id}): {why}";
            if (e.Id <= 0) return Bad("id must be positive.");
            if (!expenseIds.Add(e.Id)) return Bad("id is used twice.");
            if (MembersOf(e.GroupId) is not { } members) return Bad("groupId is not a group of the payload or an imported group of the site.");
            if (string.IsNullOrWhiteSpace(e.Description)) return Bad("description is required.");
            if (e.Date == default) return Bad("date is missing.");
            if (e.CreatedAt == default) return Bad("createdAt is missing.");
            if (!members.Contains(e.PayerMemberId)) return Bad("payerMemberId is not a member of the group.");
            if (Money(e.AmountOriginal, e.CurrencyCode, e.FxRateToUsd, e.AmountUsd) is { } money) return Bad(money);
            if (!Exact<StorageMode>(e.StorageMode, out _)) return Bad("storageMode is not ConvertedToUsd or KeepOriginal.");
            if (!Exact<SplitMode>(e.SplitMode, out _)) return Bad("splitMode is not Equal, Shares, Percentage or ExactAmounts.");

            var targets = e.Targets ?? [];
            if (targets.Count == 0) return Bad("targets is empty.");
            for (var j = 0; j < targets.Count; j++)
            {
                var t = targets[j];
                if (t is null) return Bad($"targets[{j}]: the row is missing.");
                if (!Exact<TargetType>(t.Type, out var type)) return Bad($"targets[{j}]: type is not Member or Entity.");
                if (type == TargetType.Member && !members.Contains(t.TargetId)) return Bad($"targets[{j}]: targetId is not a member of the group.");
                if (type == TargetType.Entity && !EntitiesOf(e.GroupId).Contains(t.TargetId)) return Bad($"targets[{j}]: targetId is not a couple/subgroup of the group.");
                if (t.Value is decimal v && v <= 0) return Bad($"targets[{j}]: value must be positive.");
            }

            var shares = e.Shares ?? [];
            if (shares.Count == 0) return Bad("shares is empty.");
            for (var j = 0; j < shares.Count; j++)
            {
                var s = shares[j];
                if (s is null) return Bad($"shares[{j}]: the row is missing.");
                if (!members.Contains(s.MemberId)) return Bad($"shares[{j}]: memberId is not a member of the group.");
                if (s.AmountUsd < 0 || !Decimals.HasMaxScale(s.AmountUsd, 2)) return Bad($"shares[{j}]: amountUsd must be a non-negative amount in cents.");
            }
            if (shares.Select(s => s.MemberId).Distinct().Count() != shares.Count) return Bad("shares: a member has two shares.");
            if (shares.Sum(s => s.AmountUsd) != e.AmountUsd) return Bad("shares: the amounts do not sum to amountUsd.");
        }

        var settlementIds = new HashSet<int>();
        for (var i = 0; i < settlements.Count; i++)
        {
            var s = settlements[i];
            if (s is null) return $"settlements[{i}]: the row is missing.";
            string Bad(string why) => $"settlements[{i}] (id {s.Id}): {why}";
            if (s.Id <= 0) return Bad("id must be positive.");
            if (!settlementIds.Add(s.Id)) return Bad("id is used twice.");
            if (MembersOf(s.GroupId) is not { } members) return Bad("groupId is not a group of the payload or an imported group of the site.");
            if (!members.Contains(s.FromMemberId)) return Bad("fromMemberId is not a member of the group.");
            if (!members.Contains(s.ToMemberId)) return Bad("toMemberId is not a member of the group.");
            if (s.FromMemberId == s.ToMemberId) return Bad("toMemberId is the same member as fromMemberId.");
            if (s.Date == default) return Bad("date is missing.");
            if (s.CreatedAt == default) return Bad("createdAt is missing.");
            if (Money(s.AmountOriginal, s.CurrencyCode, s.FxRateToUsd, s.AmountUsd) is { } money) return Bad(money);
        }
        return null;
    }

    /// <summary>
    /// The rules every amount meets, named by column: a known currency, a positive amount in cents, a positive rate
    /// (exactly 1 for USD) of at most 8 decimals, and the USD amount the rate gives, half-up to the cent.
    /// </summary>
    private static string? Money(decimal amount, string currency, decimal fx, decimal amountUsd)
    {
        if (!Currencies.IsValid(currency)) return "currencyCode is not a known currency.";
        if (amount <= 0 || !Decimals.HasMaxScale(amount, 2)) return "amountOriginal must be positive, with at most 2 decimals.";
        if (fx <= 0 || !Decimals.HasMaxScale(fx, 8)) return "fxRateToUsd must be positive, with at most 8 decimals.";
        if (currency == Currencies.Settlement && fx != 1m) return "fxRateToUsd must be 1 for USD.";
        if (amountUsd != SplitCalculator.ComputeAmountUsd(amount, fx)) return "amountUsd is not amountOriginal times fxRateToUsd, half-up to the cent.";
        return null;
    }

    // ---------------------------------------------------------------- never onto the site's own records

    private static string? Conflict(List<ImportGroup> groups, List<ImportExpense> expenses, List<ImportSettlement> settlements,
        IReadOnlyDictionary<int, Group> existingGroups, List<Expense> existingExpenses, List<Settlement> existingSettlements)
    {
        var native = existingGroups.Values.Where(g => !g.Imported).ToList();
        var nativeIds = native.Select(g => g.Id).ToHashSet();
        if (groups.FirstOrDefault(g => nativeIds.Contains(g.Id)) is { } group)
            return $"group {group.Id} is a group the site made itself; nothing was written.";

        var nativeMembers = native.SelectMany(g => g.Members).Select(m => m.Id).ToHashSet();
        if (groups.SelectMany(g => g.Members ?? []).FirstOrDefault(m => nativeMembers.Contains(m.Id)) is { } member)
            return $"member {member.Id} is a member of a group the site made itself; nothing was written.";

        var nativeEntities = native.SelectMany(g => g.Entities).Select(e => e.Id).ToHashSet();
        if (groups.SelectMany(g => g.Entities ?? []).FirstOrDefault(e => nativeEntities.Contains(e.Id)) is { } entity)
            return $"couple/subgroup {entity.Id} belongs to a group the site made itself; nothing was written.";

        // an expense or settlement id the site already has is skipped only when it is the same record (same group);
        // in another group, or in a group the site made itself, it is a clash
        var expenseGroups = existingExpenses.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First().GroupId);
        if (expenses.FirstOrDefault(e => expenseGroups.TryGetValue(e.Id, out var at) && (at != e.GroupId || nativeIds.Contains(at))) is { } expense)
            return $"expense {expense.Id} is already used by another record of the site; nothing was written.";

        var settlementGroups = existingSettlements.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First().GroupId);
        if (settlements.FirstOrDefault(s => settlementGroups.TryGetValue(s.Id, out var at) && (at != s.GroupId || nativeIds.Contains(at))) is { } settlement)
            return $"settlement {settlement.Id} is already used by another record of the site; nothing was written.";

        return null;
    }

    // ---------------------------------------------------------------- mapping

    private static Group ToGroup(ImportGroup g)
    {
        var group = new Group
        {
            Id = g.Id,
            Name = g.Name.Trim(),
            BaseCurrency = g.BaseCurrency,
            CreatedByUserId = 0, // matches no caller; becomes the account that claims CreatorMemberId
            CreatedAt = Utc(g.CreatedAt),
            Imported = true,
            CreatorMemberId = g.CreatorMemberId,
        };
        foreach (var m in g.Members ?? [])
        {
            group.Members.Add(new Member { Id = m.Id, GroupId = g.Id, Name = m.Name.Trim(), Email = DelatoCaller.NormalizeEmail(m.Email) });
        }
        foreach (var e in g.Entities ?? [])
        {
            Exact<EntityKind>(e.Kind, out var kind);
            var entity = new GroupEntity { Id = e.Id, GroupId = g.Id, Name = e.Name.Trim(), Kind = kind };
            entity.EntityMembers.AddRange((e.MemberIds ?? []).Distinct().OrderBy(x => x).Select(id => new EntityMember { GroupEntityId = e.Id, MemberId = id }));
            group.Entities.Add(entity);
        }
        return group;
    }

    private static Expense ToExpense(ImportExpense e)
    {
        Exact<StorageMode>(e.StorageMode, out var storage);
        Exact<SplitMode>(e.SplitMode, out var split);
        return new Expense
        {
            Id = e.Id,
            GroupId = e.GroupId,
            Description = e.Description.Trim(),
            Date = e.Date,
            PayerMemberId = e.PayerMemberId,
            AmountOriginal = e.AmountOriginal,
            CurrencyCode = e.CurrencyCode,
            FxRateToUsd = e.FxRateToUsd,
            AmountUsd = e.AmountUsd,
            StorageMode = storage,
            SplitMode = split,
            CreatedAt = Utc(e.CreatedAt),
            Targets = (e.Targets ?? []).Select((t, i) =>
            {
                Exact<TargetType>(t.Type, out var type);
                return new ExpenseTarget { Id = i + 1, ExpenseId = e.Id, Type = type, TargetId = t.TargetId, Value = t.Value };
            }).ToList(),
            Shares = (e.Shares ?? []).OrderBy(s => s.MemberId).Select((s, i) => new ExpenseShare
            {
                Id = i + 1, ExpenseId = e.Id, MemberId = s.MemberId, AmountUsd = s.AmountUsd,
            }).ToList(),
        };
    }

    private static Settlement ToSettlement(ImportSettlement s) => new()
    {
        Id = s.Id,
        GroupId = s.GroupId,
        FromMemberId = s.FromMemberId,
        ToMemberId = s.ToMemberId,
        AmountOriginal = s.AmountOriginal,
        CurrencyCode = s.CurrencyCode,
        FxRateToUsd = s.FxRateToUsd,
        AmountUsd = s.AmountUsd,
        Date = s.Date,
        CreatedAt = Utc(s.CreatedAt),
    };

    /// <summary>A creation time as UTC: one written with an offset or Z is converted; one without is taken as UTC.</summary>
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };
}
