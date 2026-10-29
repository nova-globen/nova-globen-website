namespace Delato.Shared.Domain;

public record PairDebt(int FromMemberId, int ToMemberId, decimal AmountUsd);

/// <summary>Pure balance math over expenses + settlements. All USD.</summary>
public static class BalanceCalculator
{
    /// <summary>net(member) = paid − share + settlementsSent − settlementsReceived. Positive ⇒ is owed.</summary>
    public static Dictionary<int, decimal> MemberNets(
        IEnumerable<Expense> expenses, IEnumerable<Settlement> settlements, IEnumerable<int> allMemberIds)
    {
        var cents = allMemberIds.Distinct().ToDictionary(id => id, _ => 0L);
        void Add(int id, long c) { cents.TryGetValue(id, out var cur); cents[id] = cur + c; }

        foreach (var e in expenses)
        {
            Add(e.PayerMemberId, Money.ToCents(e.AmountUsd));
            foreach (var s in e.Shares) Add(s.MemberId, -Money.ToCents(s.AmountUsd));
        }
        foreach (var s in settlements)
        {
            Add(s.FromMemberId, Money.ToCents(s.AmountUsd));
            Add(s.ToMemberId, -Money.ToCents(s.AmountUsd));
        }
        return cents.ToDictionary(kv => kv.Key, kv => Money.FromCents(kv.Value));
    }

    /// <summary>
    /// Raw pairwise debts: for each expense, every participant (except the payer) owes the payer
    /// their share; settlements reduce the debt from → to. Netted per pair, deterministic order.
    /// </summary>
    public static List<PairDebt> PairwiseDebts(IEnumerable<Expense> expenses, IEnumerable<Settlement> settlements)
    {
        // key: (a,b) with a<b, value: cents b owes a (positive) or a owes b (negative)
        var pair = new Dictionary<(int A, int B), long>();
        void Owe(int debtor, int creditor, long cents)
        {
            if (debtor == creditor || cents == 0) return;
            var (a, b) = debtor < creditor ? (debtor, creditor) : (creditor, debtor);
            long signed = debtor > creditor ? cents : -cents; // positive means b owes a
            pair.TryGetValue((a, b), out var cur);
            pair[(a, b)] = cur + signed;
        }

        foreach (var e in expenses)
            foreach (var s in e.Shares)
                Owe(s.MemberId, e.PayerMemberId, Money.ToCents(s.AmountUsd));

        foreach (var s in settlements)
            Owe(s.ToMemberId, s.FromMemberId, Money.ToCents(s.AmountUsd)); // payment reduces from's debt

        return pair
            .Where(kv => kv.Value != 0)
            .Select(kv => kv.Value > 0
                ? new PairDebt(kv.Key.B, kv.Key.A, Money.FromCents(kv.Value))
                : new PairDebt(kv.Key.A, kv.Key.B, Money.FromCents(-kv.Value)))
            .OrderBy(d => d.FromMemberId).ThenBy(d => d.ToMemberId)
            .ToList();
    }

    /// <summary>
    /// Aggregate member nets to entity level. Members not covered by any given entity become
    /// singleton pseudo-entities with id = -memberId. Callers pass disjoint entity sets (couples, or a
    /// chosen partition); should entities overlap, a member counts toward the first entity that lists them only.
    /// </summary>
    public static Dictionary<int, decimal> EntityNets(
        IReadOnlyDictionary<int, decimal> memberNets,
        IEnumerable<(int EntityId, IReadOnlyList<int> MemberIds)> entities)
    {
        var assigned = new HashSet<int>();
        var result = new Dictionary<int, long>();
        foreach (var (entityId, memberIds) in entities)
        {
            long sum = 0;
            foreach (var m in memberIds)
            {
                if (!assigned.Add(m)) continue;
                if (memberNets.TryGetValue(m, out var net)) sum += Money.ToCents(net);
            }
            result[entityId] = sum;
        }
        foreach (var (m, net) in memberNets)
            if (!assigned.Contains(m))
                result[-m] = Money.ToCents(net);
        return result.ToDictionary(kv => kv.Key, kv => Money.FromCents(kv.Value));
    }
}
