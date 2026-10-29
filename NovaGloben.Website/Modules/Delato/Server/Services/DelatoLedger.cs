using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Server.Services;

/// <summary>The site's last issued ids. Ids are per site and never reused.</summary>
public sealed class IdCounters
{
    public int Group { get; set; }
    public int Member { get; set; }
    public int Entity { get; set; }
    public int Expense { get; set; }
    public int Settlement { get; set; }
}

/// <summary>
/// Delato's groups, members, couples and subgroups, expenses, settlements and reports on one site: every rule of the
/// API, without HTTP, so the tests drive it directly. The controllers only resolve the site and the caller.
/// </summary>
/// <remarks>
/// <para>Access: a caller sees a group they created, or one with a member that is theirs - a member whose
/// <see cref="Member.UserId"/> is theirs, or an unclaimed member whose e-mail equals the caller's CONFIRMED e-mail. The
/// first time a confirmed caller is matched by e-mail, the member is claimed (its UserId set). An unconfirmed account
/// is never matched by e-mail, so it sees no linked data.</para>
/// <para>Money: balances, transfers and settle-up are in USD; a group's BaseCurrency is display-only.</para>
/// <para>Writes are serialised within the process, so ids are issued once and a group is not written by two calls at
/// once. The service therefore runs as ONE process; scaling it out needs the store's own concurrency first.</para>
/// </remarks>
public sealed class DelatoLedger(DelatoStore store, TimeProvider clock)
{
    private static SemaphoreSlim Writes => DelatoWrites.Gate;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    // ---------------------------------------------------------------- groups

    public async Task<Outcome> CreateGroupAsync(int siteId, DelatoCaller caller, GroupCreateRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Outcome.Validation("Group name is required.");
        await Writes.WaitAsync(ct);
        try
        {
            var ids = await IdsAsync(siteId, ct);
            var group = new Group
            {
                Id = ++ids.Group,
                Name = req.Name.Trim(),
                BaseCurrency = Currencies.Settlement,
                CreatedByUserId = caller.UserId,
                CreatedAt = Now,
            };
            group.Members.Add(new Member
            {
                Id = ++ids.Member,
                GroupId = group.Id,
                Name = caller.Name,
                // claimed only with a confirmed e-mail (ruling c); the creator reaches the group as its creator anyway
                UserId = caller.ConfirmedEmail is null ? null : caller.UserId,
                Email = caller.ConfirmedEmail,
            });
            await store.StoreAsync(siteId, DelatoKind.Ids, 0, 0, ids, ct);
            await store.StoreAsync(siteId, DelatoKind.Group, group.Id, 0, group, ct);
            return Outcome.Created(ToDto(group));
        }
        finally
        {
            Writes.Release();
        }
    }

    public async Task<Outcome> ListGroupsAsync(int siteId, DelatoCaller caller, CancellationToken ct = default)
    {
        var groups = await store.AllAsync<Group>(siteId, DelatoKind.Group, ct);
        var result = new List<GroupSummaryDto>();
        foreach (var group in groups.Where(g => CanSee(g, caller)).OrderBy(g => g.Id))
        {
            var (expenses, settlements) = await HistoryAsync(siteId, group.Id, ct);
            var nets = BalanceCalculator.MemberNets(expenses, settlements, group.Members.Select(m => m.Id));
            var me = FindMe(group, caller);
            var myNet = me is not null && nets.TryGetValue(me.Id, out var n) ? n : 0m;
            result.Add(new GroupSummaryDto(group.Id, group.Name, group.BaseCurrency, group.CreatedByUserId, group.Members.Count, myNet));
        }
        return Outcome.Ok(result);
    }

    public async Task<Outcome> GetGroupAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default)
    {
        var (group, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        return Outcome.Ok(new GroupDetailDto(group!.Id, group.Name, group.BaseCurrency, group.CreatedByUserId,
            group.Members.OrderBy(m => m.Id).Select(m => ToDto(m, caller)).ToList(),
            group.Entities.OrderBy(e => e.Id).Select(ToDto).ToList()));
    }

    public async Task<Outcome> RenameGroupAsync(int siteId, DelatoCaller caller, int groupId, GroupCreateRequest req, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return Outcome.Validation("Group name is required.");
        return await WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            group.Name = req.Name.Trim();
            await store.StoreAsync(siteId, DelatoKind.Group, group.Id, 0, group, ct);
            return Outcome.Ok(ToDto(group));
        }, ct);
    }

    public Task<Outcome> DeleteGroupAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            if (group.CreatedByUserId != caller.UserId)
                return Outcome.Forbidden("Only the group creator can delete the group.");
            var (expenses, settlements) = await HistoryAsync(siteId, groupId, ct);
            foreach (var e in expenses) await store.DeleteAsync(siteId, DelatoKind.Expense, groupId, e.Id, ct);
            foreach (var s in settlements) await store.DeleteAsync(siteId, DelatoKind.Settlement, groupId, s.Id, ct);
            await store.DeleteAsync(siteId, DelatoKind.Group, groupId, 0, ct);
            return Outcome.NoContent();
        }, ct);

    // ---------------------------------------------------------------- members

    public Task<Outcome> AddMemberAsync(int siteId, DelatoCaller caller, int groupId, MemberCreateRequest req, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Outcome.Validation("Member name is required.");
            var email = DelatoCaller.NormalizeEmail(req.Email);
            if (email is not null && group.Members.Any(m => m.Email == email))
                return Outcome.Conflict("CONFLICT", "A member with this email is already in the group.");

            var ids = await IdsAsync(siteId, ct);
            // Never claimed here: the account behind the address claims it by signing in with that e-mail confirmed.
            var member = new Member { Id = ++ids.Member, GroupId = groupId, Name = req.Name.Trim(), Email = email };
            group.Members.Add(member);
            await store.StoreAsync(siteId, DelatoKind.Ids, 0, 0, ids, ct);
            await store.StoreAsync(siteId, DelatoKind.Group, groupId, 0, group, ct);
            return Outcome.Created(ToDto(member, caller));
        }, ct);

    public async Task<Outcome> ListMembersAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default)
    {
        var (group, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        return Outcome.Ok(group!.Members.OrderBy(m => m.Id).Select(m => ToDto(m, caller)).ToList());
    }

    public Task<Outcome> DeleteMemberAsync(int siteId, DelatoCaller caller, int groupId, int memberId, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            var member = group.Members.FirstOrDefault(m => m.Id == memberId);
            if (member is null) return Outcome.NotFound("Member not found.");

            var (expenses, settlements) = await HistoryAsync(siteId, groupId, ct);
            var nets = BalanceCalculator.MemberNets(expenses, settlements, group.Members.Select(m => m.Id));
            if (nets.TryGetValue(memberId, out var net) && net != 0m)
                return Outcome.Conflict("MEMBER_HAS_BALANCE", $"Member has a non-zero balance ({net} USD). Settle up first.");
            if (expenses.Any(e => e.PayerMemberId == memberId || e.Shares.Any(s => s.MemberId == memberId)))
                return Outcome.Conflict("MEMBER_HAS_HISTORY", "Member appears in expenses and cannot be removed.");

            foreach (var entity in group.Entities)
                entity.EntityMembers.RemoveAll(em => em.MemberId == memberId);
            group.Members.Remove(member);
            await store.StoreAsync(siteId, DelatoKind.Group, groupId, 0, group, ct);
            return Outcome.NoContent();
        }, ct);

    // ---------------------------------------------------------------- couples and subgroups

    public Task<Outcome> AddEntityAsync(int siteId, DelatoCaller caller, int groupId, EntityCreateRequest req, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            if (string.IsNullOrWhiteSpace(req.Name)) return Outcome.Validation("Entity name is required.");
            if (!Enum.TryParse<EntityKind>(req.Kind, ignoreCase: false, out var kind) || !Enum.IsDefined(kind))
                return Outcome.Validation("Kind must be 'Couple' or 'Subgroup'.");

            var memberIds = (req.MemberIds ?? []).Distinct().OrderBy(x => x).ToList();
            if (kind == EntityKind.Couple && memberIds.Count != 2)
                return Outcome.Validation("A couple must have exactly 2 members.");
            if (kind == EntityKind.Subgroup && memberIds.Count < 2)
                return Outcome.Validation("A subgroup must have at least 2 members.");

            var groupMemberIds = group.Members.Select(m => m.Id).ToHashSet();
            if (memberIds.Any(id => !groupMemberIds.Contains(id)))
                return Outcome.Validation("All members must belong to the group.");

            if (kind == EntityKind.Couple)
            {
                var inCouple = group.Entities
                    .Where(e => e.Kind == EntityKind.Couple)
                    .SelectMany(e => e.EntityMembers.Select(em => em.MemberId))
                    .ToHashSet();
                if (memberIds.Any(inCouple.Contains))
                    return Outcome.Conflict("CONFLICT", "A member can belong to at most one couple.");
            }

            var ids = await IdsAsync(siteId, ct);
            var entity = new GroupEntity { Id = ++ids.Entity, GroupId = groupId, Name = req.Name.Trim(), Kind = kind };
            entity.EntityMembers.AddRange(memberIds.Select(id => new EntityMember { GroupEntityId = entity.Id, MemberId = id }));
            group.Entities.Add(entity);
            await store.StoreAsync(siteId, DelatoKind.Ids, 0, 0, ids, ct);
            await store.StoreAsync(siteId, DelatoKind.Group, groupId, 0, group, ct);
            return Outcome.Created(ToDto(entity));
        }, ct);

    public async Task<Outcome> ListEntitiesAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default)
    {
        var (group, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        return Outcome.Ok(group!.Entities.OrderBy(e => e.Id).Select(ToDto).ToList());
    }

    public Task<Outcome> DeleteEntityAsync(int siteId, DelatoCaller caller, int groupId, int entityId, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            var entity = group.Entities.FirstOrDefault(e => e.Id == entityId);
            if (entity is null) return Outcome.NotFound("Entity not found.");
            var (expenses, _) = await HistoryAsync(siteId, groupId, ct);
            if (expenses.Any(e => e.Targets.Any(t => t.Type == TargetType.Entity && t.TargetId == entityId)))
                return Outcome.Conflict("CONFLICT", "Entity is referenced by expenses and cannot be deleted.");
            group.Entities.Remove(entity);
            await store.StoreAsync(siteId, DelatoKind.Group, groupId, 0, group, ct);
            return Outcome.NoContent();
        }, ct);

    // ---------------------------------------------------------------- expenses

    public Task<Outcome> AddExpenseAsync(int siteId, DelatoCaller caller, int groupId, ExpenseCreateRequest req, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            var (expense, error) = BuildExpense(group, req, existing: null);
            if (error is not null) return error;
            var ids = await IdsAsync(siteId, ct);
            expense!.Id = ++ids.Expense;
            expense.CreatedAt = Now;
            await store.StoreAsync(siteId, DelatoKind.Ids, 0, 0, ids, ct);
            await store.StoreAsync(siteId, DelatoKind.Expense, groupId, expense.Id, expense, ct);
            return Outcome.Created(ToDto(expense));
        }, ct);

    public async Task<Outcome> ListExpensesAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default)
    {
        var (_, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        var (expenses, _) = await HistoryAsync(siteId, groupId, ct);
        return Outcome.Ok(expenses.OrderByDescending(e => e.Date).ThenByDescending(e => e.Id).Select(ToDto).ToList());
    }

    public async Task<Outcome> GetExpenseAsync(int siteId, DelatoCaller caller, int groupId, int expenseId, CancellationToken ct = default)
    {
        var (_, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        var expense = await store.LoadAsync<Expense>(siteId, DelatoKind.Expense, groupId, expenseId, ct);
        return expense is null ? Outcome.NotFound("Expense not found.") : Outcome.Ok(ToDto(expense));
    }

    public Task<Outcome> UpdateExpenseAsync(int siteId, DelatoCaller caller, int groupId, int expenseId, ExpenseCreateRequest req, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            var existing = await store.LoadAsync<Expense>(siteId, DelatoKind.Expense, groupId, expenseId, ct);
            if (existing is null) return Outcome.NotFound("Expense not found.");
            var (expense, error) = BuildExpense(group, req, existing);
            if (error is not null) return error;
            await store.StoreAsync(siteId, DelatoKind.Expense, groupId, expenseId, expense!, ct);
            return Outcome.Ok(ToDto(expense!));
        }, ct);

    public Task<Outcome> DeleteExpenseAsync(int siteId, DelatoCaller caller, int groupId, int expenseId, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async _ =>
        {
            var existing = await store.LoadAsync<Expense>(siteId, DelatoKind.Expense, groupId, expenseId, ct);
            if (existing is null) return Outcome.NotFound("Expense not found.");
            await store.DeleteAsync(siteId, DelatoKind.Expense, groupId, expenseId, ct);
            return Outcome.NoContent();
        }, ct);

    /// <summary>Validates the request and either builds a new expense or rewrites <paramref name="existing"/>.</summary>
    private static (Expense? Expense, Outcome? Error) BuildExpense(Group group, ExpenseCreateRequest req, Expense? existing)
    {
        if (string.IsNullOrWhiteSpace(req.Description)) return (null, Outcome.Validation("Description is required."));
        if (req.Amount <= 0) return (null, Outcome.Validation("Amount must be positive."));
        if (!Decimals.HasMaxScale(req.Amount, 2)) return (null, Outcome.Validation("Amount can have at most 2 decimal places."));
        if (!Currencies.IsValid(req.CurrencyCode)) return (null, Outcome.Validation($"Unknown currency '{req.CurrencyCode}'."));
        if (!group.Members.Any(m => m.Id == req.PayerMemberId))
            return (null, Outcome.Validation("Payer must be a member of the group."));
        if (!Enum.TryParse<SplitMode>(req.SplitMode, out var splitMode) || !Enum.IsDefined(splitMode))
            return (null, Outcome.Validation("splitMode must be Equal, Shares, Percentage or ExactAmounts."));
        if (req.Targets is null || req.Targets.Count == 0)
            return (null, Outcome.Validation("At least one split target is required."));

        decimal fxRate;
        StorageMode storageMode;
        if (req.CurrencyCode == Currencies.Settlement)
        {
            fxRate = 1m;
            storageMode = StorageMode.KeepOriginal;
        }
        else
        {
            if (req.FxRateToUsd is null or <= 0)
                return (null, Outcome.Validation("fxRateToUsd is required for non-USD expenses (1 unit = ? USD)."));
            fxRate = req.FxRateToUsd.Value;
            if (!Decimals.HasMaxScale(fxRate, 8))
                return (null, Outcome.Validation("fxRateToUsd can have at most 8 decimal places."));
            // optional: a request without it means the natural choice, converted to USD
            if (string.IsNullOrWhiteSpace(req.StorageMode))
                storageMode = StorageMode.ConvertedToUsd;
            else if (!Enum.TryParse(req.StorageMode, out storageMode) || !Enum.IsDefined(storageMode))
                return (null, Outcome.Validation("storageMode must be ConvertedToUsd or KeepOriginal."));
        }

        var targets = new List<SplitTargetInput>();
        var memberIds = group.Members.Select(m => m.Id).ToHashSet();
        var entityMap = group.Entities.ToDictionary(
            e => e.Id, e => (IReadOnlyList<int>)e.EntityMembers.Select(em => em.MemberId).ToList());
        foreach (var t in req.Targets)
        {
            if (!Enum.TryParse<TargetType>(t.Type, out var type) || !Enum.IsDefined(type))
                return (null, Outcome.Validation("Target type must be Member or Entity."));
            if (type == TargetType.Member && !memberIds.Contains(t.Id))
                return (null, Outcome.Validation($"Member {t.Id} is not in the group."));
            if (type == TargetType.Entity && !entityMap.ContainsKey(t.Id))
                return (null, Outcome.Validation($"Entity {t.Id} is not in the group."));
            if (splitMode == SplitMode.ExactAmounts && t.Value is decimal v && !Decimals.HasMaxScale(v, 2))
                return (null, Outcome.Validation("Exact amounts can have at most 2 decimal places."));
            targets.Add(new SplitTargetInput(type, t.Id, t.Value));
        }

        List<MemberShare> shares;
        try
        {
            shares = SplitCalculator.Compute(req.Amount, fxRate, splitMode, targets, entityMap);
        }
        catch (DomainException ex)
        {
            return (null, Outcome.FromDomain(ex));
        }

        var expense = existing ?? new Expense { GroupId = group.Id, Description = "", CurrencyCode = Currencies.Settlement };
        expense.Description = req.Description.Trim();
        expense.Date = req.Date;
        expense.PayerMemberId = req.PayerMemberId;
        expense.AmountOriginal = req.Amount;
        expense.CurrencyCode = req.CurrencyCode;
        expense.FxRateToUsd = fxRate;
        expense.AmountUsd = SplitCalculator.ComputeAmountUsd(req.Amount, fxRate);
        expense.StorageMode = storageMode;
        expense.SplitMode = splitMode;
        // targets keep the order they were given in (numbered 1..n); shares are per member, ascending
        expense.Targets = targets.Select((t, i) => new ExpenseTarget
        {
            Id = i + 1, ExpenseId = expense.Id, Type = t.Type, TargetId = t.Id, Value = t.Value,
        }).ToList();
        expense.Shares = shares.Select((s, i) => new ExpenseShare
        {
            Id = i + 1, ExpenseId = expense.Id, MemberId = s.MemberId, AmountUsd = s.AmountUsd,
        }).ToList();
        return (expense, null);
    }

    // ---------------------------------------------------------------- settlements

    public Task<Outcome> AddSettlementAsync(int siteId, DelatoCaller caller, int groupId, SettlementCreateRequest req, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async group =>
        {
            var memberIds = group.Members.Select(m => m.Id).ToHashSet();
            if (!memberIds.Contains(req.FromMemberId) || !memberIds.Contains(req.ToMemberId))
                return Outcome.Validation("Both members must belong to the group.");
            if (req.FromMemberId == req.ToMemberId)
                return Outcome.Validation("A member cannot pay themselves.");
            if (req.Amount <= 0) return Outcome.Validation("Amount must be positive.");
            if (!Decimals.HasMaxScale(req.Amount, 2)) return Outcome.Validation("Amount can have at most 2 decimal places.");
            if (!Currencies.IsValid(req.CurrencyCode)) return Outcome.Validation($"Unknown currency '{req.CurrencyCode}'.");

            var fxRate = 1m;
            if (req.CurrencyCode != Currencies.Settlement)
            {
                if (req.FxRateToUsd is null or <= 0)
                    return Outcome.Validation("fxRateToUsd is required for non-USD settlements.");
                fxRate = req.FxRateToUsd.Value;
                if (!Decimals.HasMaxScale(fxRate, 8))
                    return Outcome.Validation("fxRateToUsd can have at most 8 decimal places.");
            }

            var ids = await IdsAsync(siteId, ct);
            var settlement = new Settlement
            {
                Id = ++ids.Settlement,
                GroupId = groupId,
                FromMemberId = req.FromMemberId,
                ToMemberId = req.ToMemberId,
                AmountOriginal = req.Amount,
                CurrencyCode = req.CurrencyCode,
                FxRateToUsd = fxRate,
                AmountUsd = SplitCalculator.ComputeAmountUsd(req.Amount, fxRate),
                Date = req.Date,
                CreatedAt = Now,
            };
            await store.StoreAsync(siteId, DelatoKind.Ids, 0, 0, ids, ct);
            await store.StoreAsync(siteId, DelatoKind.Settlement, groupId, settlement.Id, settlement, ct);
            return Outcome.Created(ToDto(settlement));
        }, ct);

    public async Task<Outcome> ListSettlementsAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default)
    {
        var (_, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        var (_, settlements) = await HistoryAsync(siteId, groupId, ct);
        return Outcome.Ok(settlements.OrderByDescending(s => s.Date).ThenByDescending(s => s.Id).Select(ToDto).ToList());
    }

    public Task<Outcome> DeleteSettlementAsync(int siteId, DelatoCaller caller, int groupId, int settlementId, CancellationToken ct = default) =>
        WriteGroupAsync(siteId, caller, groupId, async _ =>
        {
            var existing = await store.LoadAsync<Settlement>(siteId, DelatoKind.Settlement, groupId, settlementId, ct);
            if (existing is null) return Outcome.NotFound("Settlement not found.");
            await store.DeleteAsync(siteId, DelatoKind.Settlement, groupId, settlementId, ct);
            return Outcome.NoContent();
        }, ct);

    // ---------------------------------------------------------------- reports (USD)

    public async Task<Outcome> ReportAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct = default)
    {
        var (group, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        var (expenses, settlements) = await HistoryAsync(siteId, groupId, ct);

        var nets = BalanceCalculator.MemberNets(expenses, settlements, group!.Members.Select(m => m.Id));
        var memberName = group.Members.ToDictionary(m => m.Id, m => m.Name);

        var memberBalances = nets
            .OrderBy(kv => kv.Key)
            .Select(kv => new MemberBalanceDto(kv.Key, memberName.GetValueOrDefault(kv.Key, "?"), kv.Value))
            .ToList();

        var pairwise = BalanceCalculator.PairwiseDebts(expenses, settlements)
            .Select(d => new MemberTransferDto(d.FromMemberId, d.ToMemberId, d.AmountUsd)).ToList();

        var suggested = SettlementSimplifier.Simplify(nets)
            .Select(t => new MemberTransferDto(t.FromId, t.ToId, t.AmountUsd)).ToList();

        // entity balances: real entities only, each a plain sum of its members' nets - entities may overlap, so these
        // need not sum to zero
        var entityBalances = group.Entities
            .OrderBy(e => e.Id)
            .Select(e => new EntityBalanceDto(e.Id, e.Name, e.Kind.ToString(),
                Money.FromCents(e.EntityMembers.Sum(em => Money.ToCents(nets.GetValueOrDefault(em.MemberId, 0m))))))
            .ToList();

        // transfers need a zero-sum partition: couples first, then subgroups by id; members covered by none become
        // singleton pseudo-entities (id = -memberId)
        var entityDefs = group.Entities
            .OrderBy(e => e.Kind == EntityKind.Couple ? 0 : 1).ThenBy(e => e.Id)
            .Select(e => (e.Id, (IReadOnlyList<int>)e.EntityMembers.Select(em => em.MemberId).OrderBy(x => x).ToList()))
            .ToList();
        var entityTransfers = SettlementSimplifier.Simplify(BalanceCalculator.EntityNets(nets, entityDefs))
            .Select(t => new EntityTransferDto(t.FromId, t.ToId, t.AmountUsd)).ToList();

        var totalSpent = Money.FromCents(expenses.Sum(e => Money.ToCents(e.AmountUsd)));

        return Outcome.Ok(new GroupReportDto(groupId, group.BaseCurrency, totalSpent,
            memberBalances, entityBalances, pairwise, suggested, entityTransfers));
    }

    public async Task<Outcome> MySummaryAsync(int siteId, DelatoCaller caller, CancellationToken ct = default)
    {
        var groups = await store.AllAsync<Group>(siteId, DelatoKind.Group, ct);
        var result = new List<MyGroupSummaryDto>();
        long totalCents = 0;
        foreach (var group in groups.Where(g => CanSee(g, caller)).OrderBy(g => g.Id))
        {
            var me = FindMe(group, caller);
            if (me is null) continue;

            var (expenses, settlements) = await HistoryAsync(siteId, group.Id, ct);
            var nets = BalanceCalculator.MemberNets(expenses, settlements, group.Members.Select(m => m.Id));
            var transfers = SettlementSimplifier.Simplify(nets);
            var memberName = group.Members.ToDictionary(m => m.Id, m => m.Name);

            var pay = transfers.Where(t => t.FromId == me.Id)
                .Select(t => new OwedToDto(t.ToId, memberName.GetValueOrDefault(t.ToId, "?"), t.AmountUsd)).ToList();
            var receive = transfers.Where(t => t.ToId == me.Id)
                .Select(t => new OwedFromDto(t.FromId, memberName.GetValueOrDefault(t.FromId, "?"), t.AmountUsd)).ToList();

            var myNet = nets.GetValueOrDefault(me.Id, 0m);
            totalCents += Money.ToCents(myNet);
            result.Add(new MyGroupSummaryDto(group.Id, group.Name, myNet, pay, receive));
        }
        return Outcome.Ok(new MySummaryDto(Money.FromCents(totalCents), result));
    }

    // ---------------------------------------------------------------- access

    /// <summary>The caller's own member of the group: theirs by id, or an unclaimed one with their confirmed e-mail.</summary>
    public static Member? FindMe(Group group, DelatoCaller caller) =>
        caller.UserId <= 0 ? null :
        group.Members.FirstOrDefault(m => m.UserId == caller.UserId)
        ?? (caller.ConfirmedEmail is null
            ? null
            : group.Members.FirstOrDefault(m => m.UserId is null && m.Email == caller.ConfirmedEmail));

    /// <summary>
    /// Whether the caller may see the group: its creator, or the owner of one of its members. An imported group's
    /// <see cref="Group.CreatedByUserId"/> is 0 until its creator's member is claimed, and 0 never matches a caller:
    /// account ids start at 1, and a caller with no positive id is no caller (<see cref="StommeDelatoCallers"/>).
    /// </summary>
    public static bool CanSee(Group group, DelatoCaller caller) =>
        (caller.UserId > 0 && group.CreatedByUserId == caller.UserId) || FindMe(group, caller) is not null;

    /// <summary>
    /// The group, if the caller may see it; else 404 - for a missing group and for one the caller may not see alike, so
    /// a stranger cannot count groups by their sequential ids. A confirmed caller matched by e-mail claims the member here.
    /// </summary>
    private async Task<(Group? Group, Outcome? Error)> RequireGroupAsync(int siteId, DelatoCaller caller, int groupId, CancellationToken ct)
    {
        var group = await store.LoadAsync<Group>(siteId, DelatoKind.Group, groupId, 0, ct);
        if (group is null || !CanSee(group, caller)) return (null, Outcome.Error(404, "GROUP_NOT_FOUND", "Group not found."));

        var me = FindMe(group, caller);
        if (me is not null && me.UserId is null)
        {
            await Writes.WaitAsync(ct);
            try
            {
                group = await store.LoadAsync<Group>(siteId, DelatoKind.Group, groupId, 0, ct) ?? group;
                var unclaimed = FindMe(group, caller);
                if (unclaimed is not null && unclaimed.UserId is null)
                {
                    unclaimed.UserId = caller.UserId;
                    // an imported group's creator becomes the account that claims the member its creator was - in
                    // this same write, under the write gate, so the two never part
                    if (group.CreatedByUserId == 0 && group.CreatorMemberId == unclaimed.Id)
                    {
                        group.CreatedByUserId = caller.UserId;
                    }
                    await store.StoreAsync(siteId, DelatoKind.Group, groupId, 0, group, ct);
                }
            }
            finally
            {
                Writes.Release();
            }
        }
        return (group, null);
    }

    /// <summary>Runs a change to a group the caller may see, under the write gate, on a fresh read of the group.</summary>
    private async Task<Outcome> WriteGroupAsync(int siteId, DelatoCaller caller, int groupId, Func<Group, Task<Outcome>> change, CancellationToken ct)
    {
        var (_, error) = await RequireGroupAsync(siteId, caller, groupId, ct);
        if (error is not null) return error;
        await Writes.WaitAsync(ct);
        try
        {
            var group = await store.LoadAsync<Group>(siteId, DelatoKind.Group, groupId, 0, ct);
            if (group is null) return Outcome.Error(404, "GROUP_NOT_FOUND", "Group not found.");
            return await change(group);
        }
        finally
        {
            Writes.Release();
        }
    }

    private async Task<IdCounters> IdsAsync(int siteId, CancellationToken ct) =>
        await store.LoadAsync<IdCounters>(siteId, DelatoKind.Ids, 0, 0, ct) ?? new IdCounters();

    private async Task<(List<Expense> Expenses, List<Settlement> Settlements)> HistoryAsync(int siteId, int groupId, CancellationToken ct) =>
        (await store.AllWithNumberAsync<Expense>(siteId, DelatoKind.Expense, groupId, ct),
         await store.AllWithNumberAsync<Settlement>(siteId, DelatoKind.Settlement, groupId, ct));

    // ---------------------------------------------------------------- mapping

    private static GroupDto ToDto(Group g) => new(g.Id, g.Name, g.BaseCurrency, g.CreatedByUserId);

    private static MemberDto ToDto(Member m, DelatoCaller caller) =>
        new(m.Id, m.GroupId, m.Name, m.UserId, m.Email, m.UserId == caller.UserId);

    private static EntityDto ToDto(GroupEntity e) =>
        new(e.Id, e.GroupId, e.Name, e.Kind.ToString(), e.EntityMembers.Select(em => em.MemberId).OrderBy(x => x).ToList());

    private static ExpenseDto ToDto(Expense e) => new(
        e.Id, e.GroupId, e.Description, e.Date, e.PayerMemberId,
        e.AmountOriginal, e.CurrencyCode, e.FxRateToUsd, e.AmountUsd,
        e.StorageMode.ToString(), e.SplitMode.ToString(),
        e.Targets.OrderBy(t => t.Id).Select(t => new ExpenseTargetDto(t.Type.ToString(), t.TargetId, t.Value)).ToList(),
        e.Shares.OrderBy(s => s.MemberId).Select(s => new ExpenseShareDto(s.MemberId, s.AmountUsd)).ToList());

    private static SettlementDto ToDto(Settlement s) =>
        new(s.Id, s.GroupId, s.FromMemberId, s.ToMemberId, s.AmountUsd, s.AmountOriginal, s.CurrencyCode, s.FxRateToUsd, s.Date);
}

/// <summary>
/// The one write gate of the module in this process: the ledger's writes and the import's take it, so ids are issued
/// once and a group is written by one call at a time.
/// </summary>
internal static class DelatoWrites
{
    public static readonly SemaphoreSlim Gate = new(1, 1);
}
