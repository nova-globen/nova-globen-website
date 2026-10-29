using Delato.Shared.Domain;
using Xunit;

namespace Delato.Tests.Domain;

public class SettlementSimplifierTests
{
    private static Dictionary<int, decimal> ApplyTransfers(
        IReadOnlyDictionary<int, decimal> nets, IEnumerable<Transfer> transfers)
    {
        var cents = nets.ToDictionary(kv => kv.Key, kv => Money.ToCents(kv.Value));
        foreach (var t in transfers)
        {
            Assert.True(t.AmountUsd > 0, "transfers must be strictly positive");
            cents[t.FromId] += Money.ToCents(t.AmountUsd); // paying raises the debtor's net toward 0
            cents[t.ToId] -= Money.ToCents(t.AmountUsd);   // receiving lowers the creditor's net toward 0
        }
        return cents.ToDictionary(kv => kv.Key, kv => Money.FromCents(kv.Value));
    }

    [Fact]
    public void ZeroSumNets_TransfersSettleEveryoneExactly()
    {
        var nets = new Dictionary<int, decimal>
        {
            [1] = 25.10m,
            [2] = -10.00m,
            [3] = -15.10m,
            [4] = 0m,
        };

        var transfers = SettlementSimplifier.Simplify(nets);
        var after = ApplyTransfers(nets, transfers);

        foreach (var (id, net) in after)
            Assert.True(net == 0m, $"member {id} not settled: {net}");
    }

    [Fact]
    public void ZeroSumNets_ManySeededRandomCases_AlwaysSettleToZero()
    {
        var rng = new Random(42424242);
        for (int iter = 0; iter < 200; iter++)
        {
            int n = rng.Next(2, 12);
            var nets = new Dictionary<int, decimal>();
            long sum = 0;
            for (int i = 1; i < n; i++)
            {
                long c = rng.Next(-500_000, 500_001);
                sum += c;
                nets[i] = Money.FromCents(c);
            }
            nets[n] = Money.FromCents(-sum); // force zero-sum

            var transfers = SettlementSimplifier.Simplify(nets);
            var after = ApplyTransfers(nets, transfers);
            foreach (var (id, net) in after)
                Assert.True(net == 0m, $"iter {iter}: member {id} not settled: {net}");

            // Transfer-count bound: greedy min-cash-flow removes at least one nonzero
            // participant per transfer, so at most (nonzero participants - 1) transfers.
            int nonZero = nets.Values.Count(v => v != 0m);
            Assert.True(transfers.Count <= Math.Max(0, nonZero - 1),
                $"iter {iter}: {transfers.Count} transfers for {nonZero} nonzero participants");
        }
    }

    [Fact]
    public void Deterministic_SameInputTwice_SameOutput()
    {
        var nets = new Dictionary<int, decimal>
        {
            [7] = 12.34m, [3] = -5.00m, [9] = -7.34m, [1] = 20.00m, [4] = -20.00m,
        };

        var a = SettlementSimplifier.Simplify(nets);
        var b = SettlementSimplifier.Simplify(new Dictionary<int, decimal>(nets));

        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
            Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void NCreditorsAndDebtors_AtMostNMinus1Transfers()
    {
        // 6 participants, all nonzero, zero-sum.
        var nets = new Dictionary<int, decimal>
        {
            [1] = 30m, [2] = 20m, [3] = 10m,
            [4] = -25m, [5] = -25m, [6] = -10m,
        };

        var transfers = SettlementSimplifier.Simplify(nets);

        Assert.True(transfers.Count <= 5, $"expected at most n-1=5 transfers, got {transfers.Count}");
        var after = ApplyTransfers(nets, transfers);
        Assert.All(after.Values, v => Assert.Equal(0m, v));
    }

    [Fact]
    public void ZeroNetMembers_NeverAppearInTransfers()
    {
        var nets = new Dictionary<int, decimal> { [1] = 5m, [2] = -5m, [3] = 0m };
        var transfers = SettlementSimplifier.Simplify(nets);
        Assert.Single(transfers);
        Assert.Equal(new Transfer(2, 1, 5m), transfers[0]);
    }

    [Fact]
    public void TieBreaking_EqualAmounts_LowestIdWins()
    {
        // Creditors 2 and 3 both owed 5.00: the lowest id (2) must be matched first.
        var nets = new Dictionary<int, decimal> { [5] = -10m, [3] = 5m, [2] = 5m };

        var transfers = SettlementSimplifier.Simplify(nets);

        Assert.Equal(2, transfers.Count);
        Assert.Equal(new Transfer(5, 2, 5m), transfers[0]);
        Assert.Equal(new Transfer(5, 3, 5m), transfers[1]);
    }

    [Fact]
    public void TieBreaking_EqualDebtors_LowestIdPaysFirst()
    {
        // Debtors 4 and 6 both owe 5.00: lowest id (4) is matched first.
        var nets = new Dictionary<int, decimal> { [4] = -5m, [6] = -5m, [9] = 10m };

        var transfers = SettlementSimplifier.Simplify(nets);

        Assert.Equal(2, transfers.Count);
        Assert.Equal(new Transfer(4, 9, 5m), transfers[0]);
        Assert.Equal(new Transfer(6, 9, 5m), transfers[1]);
    }

    [Fact]
    public void EmptyOrAllZeroNets_ProduceNoTransfers()
    {
        Assert.Empty(SettlementSimplifier.Simplify(new Dictionary<int, decimal>()));
        Assert.Empty(SettlementSimplifier.Simplify(new Dictionary<int, decimal> { [1] = 0m, [2] = 0m }));
    }
}
