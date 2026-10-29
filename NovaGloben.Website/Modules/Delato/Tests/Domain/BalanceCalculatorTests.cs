using Delato.Shared.Domain;
using Xunit;

namespace Delato.Tests.Domain;

public class BalanceCalculatorTests
{
    private static Expense Expense(int payer, decimal amountUsd, params (int MemberId, decimal ShareUsd)[] shares)
        => new()
        {
            Description = "test",
            CurrencyCode = "USD",
            PayerMemberId = payer,
            AmountOriginal = amountUsd,
            FxRateToUsd = 1m,
            AmountUsd = amountUsd,
            Shares = shares.Select(s => new ExpenseShare { MemberId = s.MemberId, AmountUsd = s.ShareUsd }).ToList(),
        };

    private static Settlement Settle(int from, int to, decimal amountUsd)
        => new()
        {
            CurrencyCode = "USD",
            FromMemberId = from,
            ToMemberId = to,
            AmountOriginal = amountUsd,
            FxRateToUsd = 1m,
            AmountUsd = amountUsd,
        };

    // ---- MemberNets --------------------------------------------------------

    [Fact]
    public void MemberNets_MatchesHandComputedValues()
    {
        // Expense 1: member 1 pays 90, split 30/30/30 among 1,2,3.
        // Expense 2: member 2 pays 40, split 25 (m1) / 15 (m2).
        // Settlement: member 3 pays member 1 10.
        var expenses = new[]
        {
            Expense(1, 90m, (1, 30m), (2, 30m), (3, 30m)),
            Expense(2, 40m, (1, 25m), (2, 15m)),
        };
        var settlements = new[] { Settle(3, 1, 10m) };

        var nets = BalanceCalculator.MemberNets(expenses, settlements, new[] { 1, 2, 3, 4 });

        // m1: +90 -30 -25 -10(received) = 25
        // m2: +40 -30 -15            = -5
        // m3: -30 +10(sent)          = -20
        // m4: no activity            = 0
        Assert.Equal(4, nets.Count);
        Assert.Equal(25m, nets[1]);
        Assert.Equal(-5m, nets[2]);
        Assert.Equal(-20m, nets[3]);
        Assert.Equal(0m, nets[4]);

        Assert.Equal(0m, nets.Values.Sum()); // nets over a closed group are zero-sum
    }

    [Fact]
    public void MemberNets_PayerAlsoParticipant_NetIsPaidMinusOwnShare()
    {
        var expenses = new[] { Expense(1, 100m, (1, 50m), (2, 50m)) };
        var nets = BalanceCalculator.MemberNets(expenses, Array.Empty<Settlement>(), new[] { 1, 2 });
        Assert.Equal(50m, nets[1]);
        Assert.Equal(-50m, nets[2]);
    }

    // ---- PairwiseDebts -----------------------------------------------------

    [Fact]
    public void PairwiseDebts_OppositeDebts_NetToSingleDirection()
    {
        // A(=1) owes B(=2) 10; B owes A 4 => single debt A -> B of 6.
        var expenses = new[]
        {
            Expense(2, 10m, (1, 10m)), // B paid, A's share 10 => A owes B 10
            Expense(1, 4m, (2, 4m)),   // A paid, B's share 4  => B owes A 4
        };

        var debts = BalanceCalculator.PairwiseDebts(expenses, Array.Empty<Settlement>());

        Assert.Single(debts);
        Assert.Equal(new PairDebt(1, 2, 6m), debts[0]);
    }

    [Fact]
    public void PairwiseDebts_SettlementReducesDebt_AndPayerOwnShareIgnored()
    {
        // m1 pays 90 split equally among 1,2,3: m2 and m3 each owe m1 30 (payer's own share is not a debt).
        // m2 settles 30 to m1 => only m3 -> m1 30 remains.
        var expenses = new[] { Expense(1, 90m, (1, 30m), (2, 30m), (3, 30m)) };
        var settlements = new[] { Settle(2, 1, 30m) };

        var debts = BalanceCalculator.PairwiseDebts(expenses, settlements);

        Assert.Single(debts);
        Assert.Equal(new PairDebt(3, 1, 30m), debts[0]);
    }

    [Fact]
    public void PairwiseDebts_FullyOffsetPair_IsOmitted_AndOrderIsDeterministic()
    {
        var expenses = new[]
        {
            Expense(2, 10m, (1, 10m)), // 1 owes 2: 10
            Expense(1, 10m, (2, 10m)), // 2 owes 1: 10 -> pair (1,2) cancels out
            Expense(3, 5m, (1, 5m)),   // 1 owes 3: 5
            Expense(3, 7m, (2, 7m)),   // 2 owes 3: 7
        };

        var debts = BalanceCalculator.PairwiseDebts(expenses, Array.Empty<Settlement>());

        Assert.Equal(2, debts.Count); // (1,2) omitted
        Assert.Equal(new PairDebt(1, 3, 5m), debts[0]); // ordered by FromMemberId then ToMemberId
        Assert.Equal(new PairDebt(2, 3, 7m), debts[1]);
    }

    // ---- EntityNets --------------------------------------------------------

    [Fact]
    public void EntityNets_CoupleAggregates_AndUncoveredMembersBecomeSingletonPseudoEntities()
    {
        var memberNets = new Dictionary<int, decimal> { [1] = 10m, [2] = -4m, [3] = -6m };
        var entities = new[] { (EntityId: 1, MemberIds: (IReadOnlyList<int>)new[] { 1, 2 }) };

        var nets = BalanceCalculator.EntityNets(memberNets, entities);

        Assert.Equal(2, nets.Count);
        Assert.Equal(6m, nets[1]);   // couple: 10 - 4
        Assert.Equal(-6m, nets[-3]); // singleton pseudo-entity id = -memberId
    }

    [Fact]
    public void EntityNets_MemberInTwoEntities_CountsOnlyTowardFirstListed()
    {
        // Member 2 is in entity 1 (couple, listed first) and entity 5 (subgroup):
        // couples take precedence via the assigned set — entity 5 must not double-count member 2.
        var memberNets = new Dictionary<int, decimal> { [1] = 10m, [2] = -4m, [3] = -6m };
        var entities = new[]
        {
            (EntityId: 1, MemberIds: (IReadOnlyList<int>)new[] { 1, 2 }),
            (EntityId: 5, MemberIds: (IReadOnlyList<int>)new[] { 2, 3 }),
        };

        var nets = BalanceCalculator.EntityNets(memberNets, entities);

        Assert.Equal(2, nets.Count);
        Assert.Equal(6m, nets[1]);  // 10 - 4
        Assert.Equal(-6m, nets[5]); // only member 3; member 2 already assigned to entity 1
        Assert.False(nets.ContainsKey(-2), "member 2 must not also appear as a pseudo-entity");
        Assert.False(nets.ContainsKey(-3), "member 3 is covered by entity 5");

        Assert.Equal(memberNets.Values.Sum(), nets.Values.Sum()); // aggregation preserves the total
    }

    [Fact]
    public void EntityNets_NoEntities_EveryMemberBecomesPseudoEntity()
    {
        var memberNets = new Dictionary<int, decimal> { [4] = 2.50m, [7] = -2.50m };

        var nets = BalanceCalculator.EntityNets(
            memberNets, Array.Empty<(int, IReadOnlyList<int>)>());

        Assert.Equal(2, nets.Count);
        Assert.Equal(2.50m, nets[-4]);
        Assert.Equal(-2.50m, nets[-7]);
    }
}
