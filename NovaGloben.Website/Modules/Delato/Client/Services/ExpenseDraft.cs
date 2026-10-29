using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Client.Services;

/// <summary>One problem with the draft: a text key and its arguments, shown in the page's language.</summary>
public sealed record DraftProblem(string Key, params object[] Args);

/// <summary>One possible split target in the editor: a member or a couple/subgroup, whether it takes part, and its value.</summary>
public sealed class DraftTarget
{
    public required TargetType Type { get; init; }
    public required int Id { get; init; }
    public required string Name { get; init; }
    public bool Selected { get; set; }
    public string ValueText { get; set; } = "";
}

/// <summary>
/// The expense form's state and rules, without the page: what was typed, whether it is valid, the request it becomes and
/// the per-member preview, computed with the same split math the server uses. A non-USD amount needs the rate
/// ("1 USD = ? XXX") and the choice to convert to USD or keep the original.
/// </summary>
public sealed class ExpenseDraft
{
    public string Description { get; set; } = "";
    public DateOnly Date { get; set; }
    public int PayerMemberId { get; set; }
    public string AmountText { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public string PerUsdText { get; set; } = "";
    public StorageMode Storage { get; set; } = StorageMode.ConvertedToUsd;
    public SplitMode Mode { get; set; } = SplitMode.Equal;
    public List<DraftTarget> Targets { get; } = [];

    private readonly Dictionary<int, IReadOnlyList<int>> _entityMembers = new();

    public bool IsForeign => Currency != "USD";

    /// <summary>A new draft for the group: every member and couple/subgroup offered, every member selected.</summary>
    public static ExpenseDraft For(GroupDetailDto group, DateOnly today, int? payer = null)
    {
        var draft = new ExpenseDraft { Date = today, PayerMemberId = payer ?? group.Members.FirstOrDefault()?.Id ?? 0 };
        foreach (var m in group.Members)
            draft.Targets.Add(new DraftTarget { Type = TargetType.Member, Id = m.Id, Name = m.Name, Selected = true });
        foreach (var e in group.Entities)
        {
            draft.Targets.Add(new DraftTarget { Type = TargetType.Entity, Id = e.Id, Name = e.Name });
            draft._entityMembers[e.Id] = e.MemberIds;
        }
        return draft;
    }

    /// <summary>A draft of an existing expense, for editing it.</summary>
    public static ExpenseDraft From(GroupDetailDto group, ExpenseDto expense)
    {
        var draft = For(group, expense.Date, expense.PayerMemberId);
        draft.Description = expense.Description;
        draft.AmountText = MoneyText.Plain(expense.AmountOriginal);
        draft.Currency = expense.CurrencyCode;
        draft.PerUsdText = draft.IsForeign && FxRate.ToUsdPrice(expense.FxRateToUsd) is { } price ? MoneyText.Plain(price) : "";
        draft.Storage = Enum.Parse<StorageMode>(expense.StorageMode);
        draft.Mode = Enum.Parse<SplitMode>(expense.SplitMode);
        foreach (var target in draft.Targets) target.Selected = false;
        foreach (var t in expense.Targets)
        {
            var type = Enum.Parse<TargetType>(t.Type);
            if (draft.Targets.FirstOrDefault(d => d.Type == type && d.Id == t.Id) is { } match)
            {
                match.Selected = true;
                match.ValueText = t.Value is decimal v ? MoneyText.Plain(v) : "";
            }
        }
        return draft;
    }

    public decimal? Amount => MoneyText.Parse(AmountText);

    /// <summary>The stored rate: 1 for USD, else the inverse of the typed "1 USD = ? XXX".</summary>
    public decimal? FxRateToUsd => !IsForeign ? 1m : MoneyText.Parse(PerUsdText) is { } price ? FxRate.FromUsdPrice(price) : null;

    /// <summary>What is wrong with the draft, in the order the form shows it; empty when it can be sent.</summary>
    public List<DraftProblem> Problems()
    {
        var problems = new List<DraftProblem>();
        if (string.IsNullOrWhiteSpace(Description)) problems.Add(new("ErrorDescription"));
        if (PayerMemberId <= 0) problems.Add(new("ErrorPayer"));
        if (Amount is not { } amount || amount <= 0 || Math.Round(amount, 2) != amount) problems.Add(new("ErrorAmount"));
        if (IsForeign && FxRateToUsd is null) problems.Add(new("ErrorRate"));

        var chosen = Targets.Where(t => t.Selected).ToList();
        if (chosen.Count == 0)
        {
            problems.Add(new("ErrorNoTargets"));
            return problems;
        }
        var values = chosen.Select(t => MoneyText.Parse(t.ValueText)).ToList();
        switch (Mode)
        {
            case SplitMode.Shares when values.Any(v => v is not { } s || s <= 0 || s != Math.Floor(s)):
                problems.Add(new("ErrorShares"));
                break;
            case SplitMode.Percentage when values.Any(v => v is not > 0):
                problems.Add(new("ErrorPercentValues"));
                break;
            case SplitMode.Percentage when values.Sum(v => v!.Value) != 100m:
                problems.Add(new("ErrorPercentSum", MoneyText.Plain(values.Sum(v => v!.Value))));
                break;
            case SplitMode.ExactAmounts when values.Any(v => v is not { } x || x <= 0 || Math.Round(x, 2) != x):
                problems.Add(new("ErrorExactValues"));
                break;
            case SplitMode.ExactAmounts when Amount is { } total && values.Sum(v => v!.Value) != total:
                problems.Add(new("ErrorExactSum", MoneyText.Plain(values.Sum(v => v!.Value)), MoneyText.Plain(total)));
                break;
        }
        return problems;
    }

    /// <summary>The request the draft becomes (only meaningful when <see cref="Problems"/> is empty).</summary>
    public ExpenseCreateRequest ToRequest() => new(
        Description.Trim(), Date, PayerMemberId, Amount ?? 0m, Currency,
        IsForeign ? FxRateToUsd : null,
        IsForeign ? Storage.ToString() : null,
        Mode.ToString(),
        Targets.Where(t => t.Selected)
            .Select(t => new ExpenseTargetDto(t.Type.ToString(), t.Id, Mode == SplitMode.Equal ? null : MoneyText.Parse(t.ValueText)))
            .ToList());

    /// <summary>The USD amount and each member's share as the server will compute them; null while the draft is not valid.</summary>
    public (decimal AmountUsd, List<MemberShare> Shares)? Preview()
    {
        if (Problems().Count > 0) return null;
        var request = ToRequest();
        try
        {
            var shares = SplitCalculator.Compute(request.Amount, request.FxRateToUsd ?? 1m, Mode,
                request.Targets.Select(t => new SplitTargetInput(Enum.Parse<TargetType>(t.Type), t.Id, t.Value)).ToList(),
                _entityMembers);
            return (SplitCalculator.ComputeAmountUsd(request.Amount, request.FxRateToUsd ?? 1m), shares);
        }
        catch (DomainException)
        {
            return null;
        }
    }
}
