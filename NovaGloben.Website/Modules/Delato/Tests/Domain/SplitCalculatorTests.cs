using Delato.Shared.Domain;
using Xunit;

namespace Delato.Tests.Domain;

public class SplitCalculatorTests
{
    private static readonly IReadOnlyDictionary<int, IReadOnlyList<int>> NoEntities =
        new Dictionary<int, IReadOnlyList<int>>();

    private static SplitTargetInput M(int id, decimal? value = null) => new(TargetType.Member, id, value);
    private static SplitTargetInput E(int id, decimal? value = null) => new(TargetType.Entity, id, value);

    // ---- error cases -------------------------------------------------------

    [Theory]
    [InlineData(99.0)]
    [InlineData(100.5)]
    [InlineData(40.0)]
    public void Percentage_NotSumming100_Throws_SPLIT_MISMATCH(double firstPct)
    {
        var targets = new[] { M(1, (decimal)firstPct), M(2, 30m), M(3, 20m) };
        // 99/100.5/40 + 30 + 20 != 100 (beyond the ±0.01 tolerance)
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Percentage, targets, NoEntities));
        Assert.Equal("SPLIT_MISMATCH", ex.Code);
    }

    [Fact]
    public void Percentage_WithinCentTolerance_DoesNotThrow()
    {
        // 33.33 + 33.33 + 33.335 = 99.995, |sum-100| = 0.005 <= 0.01 tolerance
        var targets = new[] { M(1, 33.33m), M(2, 33.33m), M(3, 33.335m) };
        var shares = SplitCalculator.Compute(100m, 1m, SplitMode.Percentage, targets, NoEntities);
        Assert.Equal(100m, shares.Sum(s => s.AmountUsd));
    }

    [Fact]
    public void ExactAmounts_NotSummingToAmount_Throws_SPLIT_MISMATCH()
    {
        var targets = new[] { M(1, 60m), M(2, 39.99m) };
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.ExactAmounts, targets, NoEntities));
        Assert.Equal("SPLIT_MISMATCH", ex.Code);
    }

    [Fact]
    public void ExactAmounts_MustSumToOriginalAmount_NotUsdAmount()
    {
        // Sum equals the original-currency amount; must succeed even with fx != 1.
        var targets = new[] { M(1, 60m), M(2, 40m) };
        var shares = SplitCalculator.Compute(100m, 1.25m, SplitMode.ExactAmounts, targets, NoEntities);
        Assert.Equal(125m, shares.Sum(s => s.AmountUsd));
    }

    [Fact]
    public void EmptyTargets_Throws_VALIDATION()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Equal, Array.Empty<SplitTargetInput>(), NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Fact]
    public void DuplicateTargets_Throws_VALIDATION()
    {
        var targets = new[] { M(1), M(2), M(1) };
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Equal, targets, NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Fact]
    public void SameId_MemberAndEntity_AreDistinctTargets_NotDuplicates()
    {
        var entities = new Dictionary<int, IReadOnlyList<int>> { [1] = new[] { 2, 3 } };
        var targets = new[] { M(1), E(1) };
        var shares = SplitCalculator.Compute(100m, 1m, SplitMode.Equal, targets, entities);
        Assert.Equal(100m, shares.Sum(s => s.AmountUsd));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-5.0)]
    public void NonPositiveAmount_Throws_VALIDATION(double amount)
    {
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute((decimal)amount, 1m, SplitMode.Equal, new[] { M(1) }, NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    public void NonPositiveFxRate_Throws_VALIDATION(double fx)
    {
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, (decimal)fx, SplitMode.Equal, new[] { M(1) }, NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Theory]
    [InlineData(1.5)]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    public void Shares_NonIntegerOrNonPositive_Throws_VALIDATION(double share)
    {
        var targets = new[] { M(1, (decimal)share), M(2, 1m) };
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Shares, targets, NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Fact]
    public void Shares_MissingValue_Throws_VALIDATION()
    {
        var targets = new[] { M(1, 2m), M(2) };
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Shares, targets, NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Fact]
    public void EntityTarget_WithNoMembers_Throws_VALIDATION()
    {
        var entities = new Dictionary<int, IReadOnlyList<int>> { [1] = Array.Empty<int>() };
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Equal, new[] { E(1) }, entities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    [Fact]
    public void EntityTarget_Unknown_Throws_VALIDATION()
    {
        var ex = Assert.Throws<DomainException>(() =>
            SplitCalculator.Compute(100m, 1m, SplitMode.Equal, new[] { E(42) }, NoEntities));
        Assert.Equal("VALIDATION", ex.Code);
    }

    // ---- rounding behaviour ------------------------------------------------

    [Fact]
    public void RemainderCents_GoToAscendingTargetIds_MemberBeforeEntityOnTie()
    {
        // 100.01 over Member 2 and Entity 2 (members 5,6): equal weights => 5000/5000 cents,
        // remaining 1 cent goes to Member 2 (tie on id 2, Member first).
        var entities = new Dictionary<int, IReadOnlyList<int>> { [2] = new[] { 5, 6 } };
        var targets = new[] { E(2), M(2) };
        var shares = SplitCalculator.Compute(100.01m, 1m, SplitMode.Equal, targets, entities)
            .ToDictionary(s => s.MemberId, s => s.AmountUsd);
        Assert.Equal(50.01m, shares[2]);
        Assert.Equal(25.00m, shares[5]);
        Assert.Equal(25.00m, shares[6]);
    }

    // ---- property-style checks (seeded, deterministic) ---------------------

    [Fact]
    public void Property_SumOfShares_EqualsRoundedUsdTotal_Exactly()
    {
        var rng = new Random(20260825);
        decimal[] fxRates = { 1m, 0.85m, 1.09m, 1.25m, 0.0000017m, 142.7m };

        for (int iter = 0; iter < 500; iter++)
        {
            decimal amount = rng.Next(1, 5_000_000) / 100m; // 0.01 .. 49999.99
            decimal fx = fxRates[rng.Next(fxRates.Length)];
            int n = rng.Next(1, 9);
            var ids = Enumerable.Range(0, n).Select(i => 1 + i * (1 + rng.Next(3))).Distinct().ToArray();

            SplitMode mode = (iter % 3) switch { 0 => SplitMode.Equal, 1 => SplitMode.Shares, _ => SplitMode.Percentage };
            List<SplitTargetInput> targets;
            if (mode == SplitMode.Equal)
            {
                targets = ids.Select(id => M(id)).ToList();
            }
            else if (mode == SplitMode.Shares)
            {
                targets = ids.Select(id => M(id, rng.Next(1, 10))).ToList();
            }
            else
            {
                // Percentages summing exactly to 100 (integer partition).
                var pcts = new int[ids.Length];
                int left = 100;
                for (int i = 0; i < ids.Length - 1; i++)
                {
                    int maxTake = left - (ids.Length - 1 - i); // leave >=1 for each remaining target
                    int take = maxTake <= 1 ? 1 : rng.Next(1, maxTake);
                    pcts[i] = take;
                    left -= take;
                }
                pcts[^1] = left;
                targets = ids.Select((id, i) => M(id, pcts[i])).ToList();
            }

            var shares = SplitCalculator.Compute(amount, fx, mode, targets, NoEntities);
            decimal expectedTotal = Money.Round2(amount * fx);
            Assert.True(expectedTotal == shares.Sum(s => s.AmountUsd),
                $"iter {iter}: mode={mode} amount={amount} fx={fx} sum={shares.Sum(s => s.AmountUsd)} expected={expectedTotal}");
        }
    }

    [Fact]
    public void Property_EntitySubdivisionTotals_EqualEntityShare()
    {
        var rng = new Random(987654321);

        for (int iter = 0; iter < 300; iter++)
        {
            decimal amount = rng.Next(1, 1_000_000) / 100m;
            decimal fx = (iter % 2 == 0) ? 1m : 1.1m;

            // Entity id and its members; member targets use disjoint higher ids.
            int entityId = rng.Next(1, 5);
            int entityMemberCount = rng.Next(1, 5);
            var entityMemberIds = Enumerable.Range(100, entityMemberCount).ToArray();
            var entities = new Dictionary<int, IReadOnlyList<int>> { [entityId] = entityMemberIds };

            int directCount = rng.Next(1, 4);
            var directIds = Enumerable.Range(10, directCount).ToArray(); // 10..12, never equal to entityId (1..4)

            var mode = iter % 2 == 0 ? SplitMode.Equal : SplitMode.Shares;
            decimal? V() => mode == SplitMode.Shares ? rng.Next(1, 6) : null;

            var entityValue = V();
            var directValues = directIds.Select(_ => V()).ToArray();

            var targets = new List<SplitTargetInput> { E(entityId, entityValue) };
            targets.AddRange(directIds.Select((id, i) => M(id, directValues[i])));

            var shares = SplitCalculator.Compute(amount, fx, mode, targets, entities)
                .ToDictionary(s => s.MemberId, s => s.AmountUsd);

            // Proxy computation: replace the entity target with a member of the same id.
            // Same ids, same weights => the top-level distribution is identical, so the
            // proxy member's share equals the entity's total share.
            var proxyTargets = new List<SplitTargetInput> { M(entityId, entityValue) };
            proxyTargets.AddRange(directIds.Select((id, i) => M(id, directValues[i])));
            var proxyShares = SplitCalculator.Compute(amount, fx, mode, proxyTargets, NoEntities)
                .ToDictionary(s => s.MemberId, s => s.AmountUsd);

            decimal entityTotal = entityMemberIds.Sum(m => shares[m]);
            Assert.True(proxyShares[entityId] == entityTotal,
                $"iter {iter}: entity share {proxyShares[entityId]} != subdivision total {entityTotal}");

            // Subdivision spread: members differ by at most one cent, earlier (lower) ids first.
            var subAmounts = entityMemberIds.Select(m => shares[m]).ToArray();
            Assert.True(subAmounts.Max() - subAmounts.Min() <= 0.01m);
            for (int i = 1; i < subAmounts.Length; i++)
                Assert.True(subAmounts[i - 1] >= subAmounts[i],
                    "extra cents must go to ascending member ids first");
        }
    }
}
