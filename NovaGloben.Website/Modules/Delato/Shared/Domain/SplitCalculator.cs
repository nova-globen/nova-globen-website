namespace Delato.Shared.Domain;

public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public record SplitTargetInput(TargetType Type, int Id, decimal? Value);
public record MemberShare(int MemberId, decimal AmountUsd);

public static class Money
{
    /// <summary>Round to 2 decimals, HALF_UP (away from zero).</summary>
    public static decimal Round2(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
    public static long ToCents(decimal value) => (long)Math.Round(value * 100m, 0, MidpointRounding.AwayFromZero);
    public static decimal FromCents(long cents) => cents / 100m;
}

/// <summary>
/// The split algorithm. Its specification is the test vectors (Tests/split-test-vectors.json): shares floored to cents,
/// remainder cents handed out one by one in ascending target id order (Member before Entity on ties).
/// </summary>
public static class SplitCalculator
{
    public static decimal ComputeAmountUsd(decimal amountOriginal, decimal fxRateToUsd)
        => Money.Round2(amountOriginal * fxRateToUsd);

    /// <param name="entityMembers">entityId -> ordered member ids (any order; sorted internally)</param>
    public static List<MemberShare> Compute(
        decimal amountOriginal,
        decimal fxRateToUsd,
        SplitMode mode,
        IReadOnlyList<SplitTargetInput> targets,
        IReadOnlyDictionary<int, IReadOnlyList<int>> entityMembers)
    {
        if (amountOriginal <= 0) throw new DomainException("VALIDATION", "Amount must be positive.");
        if (fxRateToUsd <= 0) throw new DomainException("VALIDATION", "FX rate must be positive.");
        if (targets.Count == 0) throw new DomainException("VALIDATION", "At least one split target is required.");
        if (targets.Select(t => (t.Type, t.Id)).Distinct().Count() != targets.Count)
            throw new DomainException("VALIDATION", "Duplicate split targets.");

        foreach (var t in targets.Where(t => t.Type == TargetType.Entity))
        {
            if (!entityMembers.TryGetValue(t.Id, out var mem) || mem.Count == 0)
                throw new DomainException("VALIDATION", $"Entity {t.Id} has no members.");
        }

        var weights = ComputeWeights(mode, targets, amountOriginal);
        long totalCents = Money.ToCents(ComputeAmountUsd(amountOriginal, fxRateToUsd));

        // Proportional distribution: floor each, then hand out remaining cents in
        // ascending target id order (Member before Entity on id ties).
        var ordered = targets
            .Select((t, i) => (Target: t, Weight: weights[i]))
            .OrderBy(x => x.Target.Id)
            .ThenBy(x => x.Target.Type == TargetType.Member ? 0 : 1)
            .ToList();

        decimal weightSum = ordered.Sum(x => x.Weight);
        if (weightSum <= 0) throw new DomainException("VALIDATION", "Split weights must be positive.");

        var cents = new long[ordered.Count];
        long assigned = 0;
        for (int i = 0; i < ordered.Count; i++)
        {
            cents[i] = (long)Math.Floor(totalCents * ordered[i].Weight / weightSum);
            assigned += cents[i];
        }
        for (int i = 0; assigned < totalCents; i = (i + 1) % ordered.Count)
        {
            cents[i]++;
            assigned++;
        }

        // Subdivide entity shares equally among members (floor + remainder to ascending member id).
        var perMember = new SortedDictionary<int, long>();
        void Add(int memberId, long c)
        {
            perMember.TryGetValue(memberId, out var cur);
            perMember[memberId] = cur + c;
        }

        for (int i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i].Target;
            if (t.Type == TargetType.Member)
            {
                Add(t.Id, cents[i]);
            }
            else
            {
                var members = entityMembers[t.Id].OrderBy(m => m).ToList();
                long baseShare = cents[i] / members.Count;
                long rem = cents[i] % members.Count;
                for (int m = 0; m < members.Count; m++)
                    Add(members[m], baseShare + (m < rem ? 1 : 0));
            }
        }

        return perMember.Select(kv => new MemberShare(kv.Key, Money.FromCents(kv.Value))).ToList();
    }

    private static decimal[] ComputeWeights(SplitMode mode, IReadOnlyList<SplitTargetInput> targets, decimal amountOriginal)
    {
        var w = new decimal[targets.Count];
        switch (mode)
        {
            case SplitMode.Equal:
                for (int i = 0; i < w.Length; i++) w[i] = 1m;
                break;
            case SplitMode.Shares:
                for (int i = 0; i < w.Length; i++)
                {
                    var v = targets[i].Value ?? throw new DomainException("VALIDATION", "Shares split requires a value per target.");
                    if (v <= 0 || v != Math.Floor(v))
                        throw new DomainException("VALIDATION", "Shares must be positive integers.");
                    w[i] = v;
                }
                break;
            case SplitMode.Percentage:
                decimal pctSum = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    var v = targets[i].Value ?? throw new DomainException("VALIDATION", "Percentage split requires a value per target.");
                    if (v <= 0) throw new DomainException("VALIDATION", "Percentages must be positive.");
                    w[i] = v;
                    pctSum += v;
                }
                if (Math.Abs(pctSum - 100m) > 0.01m)
                    throw new DomainException("SPLIT_MISMATCH", $"Percentages sum to {pctSum}, expected 100.");
                break;
            case SplitMode.ExactAmounts:
                decimal amtSum = 0;
                for (int i = 0; i < w.Length; i++)
                {
                    var v = targets[i].Value ?? throw new DomainException("VALIDATION", "ExactAmounts split requires a value per target.");
                    if (v <= 0) throw new DomainException("VALIDATION", "Exact amounts must be positive.");
                    w[i] = v;
                    amtSum += v;
                }
                if (amtSum != amountOriginal)
                    throw new DomainException("SPLIT_MISMATCH", $"Exact amounts sum to {amtSum}, expected {amountOriginal}.");
                break;
            default:
                throw new DomainException("VALIDATION", "Unknown split mode.");
        }
        return w;
    }
}
