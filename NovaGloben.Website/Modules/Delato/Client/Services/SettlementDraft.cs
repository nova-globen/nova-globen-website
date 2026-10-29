using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Client.Services;

/// <summary>
/// The settle-up form's state and rules, without the page: who pays whom, how much, in which currency (with the rate
/// "1 USD = ? XXX" outside USD), and the request it becomes. A suggested transfer prefills it in USD. The payment counts
/// in USD, the one settlement currency.
/// </summary>
public sealed class SettlementDraft
{
    public int FromMemberId { get; set; }
    public int ToMemberId { get; set; }
    public string AmountText { get; set; } = "";
    public string Currency { get; set; } = "USD";
    public string PerUsdText { get; set; } = "";
    public DateOnly Date { get; set; }

    public bool IsForeign => Currency != "USD";

    /// <summary>A draft of a suggested transfer: from, to and the USD amount.</summary>
    public static SettlementDraft From(MemberTransferDto transfer, DateOnly today) => new()
    {
        FromMemberId = transfer.FromMemberId,
        ToMemberId = transfer.ToMemberId,
        AmountText = MoneyText.Plain(transfer.AmountUsd),
        Date = today,
    };

    public decimal? Amount => MoneyText.Parse(AmountText);

    public decimal? FxRateToUsd => !IsForeign ? 1m : MoneyText.Parse(PerUsdText) is { } price ? FxRate.FromUsdPrice(price) : null;

    /// <summary>The USD the payment counts as (half-up to the cent), or null while amount or rate are missing.</summary>
    public decimal? AmountUsd => Amount is { } amount && FxRateToUsd is { } rate ? SplitCalculator.ComputeAmountUsd(amount, rate) : null;

    public List<DraftProblem> Problems()
    {
        var problems = new List<DraftProblem>();
        if (FromMemberId <= 0 || ToMemberId <= 0) problems.Add(new("ErrorSettleMembers"));
        else if (FromMemberId == ToMemberId) problems.Add(new("ErrorSettleSame"));
        if (Amount is not { } amount || amount <= 0 || Math.Round(amount, 2) != amount) problems.Add(new("ErrorAmount"));
        if (IsForeign && FxRateToUsd is null) problems.Add(new("ErrorRate"));
        return problems;
    }

    public SettlementCreateRequest ToRequest() =>
        new(FromMemberId, ToMemberId, Amount ?? 0m, Currency, IsForeign ? FxRateToUsd : null, Date);
}
