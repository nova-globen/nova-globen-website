namespace Delato.Shared.Domain;

public record Transfer(int FromId, int ToId, decimal AmountUsd);

/// <summary>
/// Min-cash-flow greedy debt simplification: repeatedly match the largest debtor with the
/// largest creditor; ties broken by ascending id. Deterministic.
/// </summary>
public static class SettlementSimplifier
{
    public static List<Transfer> Simplify(IReadOnlyDictionary<int, decimal> netUsdById)
    {
        // cents ledgers; positive = is owed money (creditor), negative = owes (debtor)
        var creditors = new List<(int Id, long Cents)>();
        var debtors = new List<(int Id, long Cents)>();
        foreach (var (id, net) in netUsdById)
        {
            long c = Money.ToCents(net);
            if (c > 0) creditors.Add((id, c));
            else if (c < 0) debtors.Add((id, -c));
        }

        var transfers = new List<Transfer>();
        while (creditors.Count > 0 && debtors.Count > 0)
        {
            int ci = IndexOfMax(creditors);
            int di = IndexOfMax(debtors);
            var cred = creditors[ci];
            var deb = debtors[di];
            long amount = Math.Min(cred.Cents, deb.Cents);
            transfers.Add(new Transfer(deb.Id, cred.Id, Money.FromCents(amount)));

            if (cred.Cents == amount) creditors.RemoveAt(ci); else creditors[ci] = (cred.Id, cred.Cents - amount);
            if (deb.Cents == amount) debtors.RemoveAt(di); else debtors[di] = (deb.Id, deb.Cents - amount);
        }
        return transfers;
    }

    private static int IndexOfMax(List<(int Id, long Cents)> list)
    {
        int best = 0;
        for (int i = 1; i < list.Count; i++)
            if (list[i].Cents > list[best].Cents ||
                (list[i].Cents == list[best].Cents && list[i].Id < list[best].Id))
                best = i;
        return best;
    }
}
