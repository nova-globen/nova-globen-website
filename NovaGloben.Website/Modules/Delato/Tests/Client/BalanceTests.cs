using System.Text.RegularExpressions;
using Delato.Client.Services;
using Delato.Client.Ui;
using Delato.Shared.Contracts;

namespace Delato.Tests;

/// <summary>The settle-up form's rules and the request it makes; a suggested transfer prefills it in USD.</summary>
public sealed class SettlementDraftTests
{
    private static readonly DateOnly Day = new(2026, 10, 29);

    [Fact]
    public void A_suggested_transfer_prefills_the_form_in_USD()
    {
        var draft = SettlementDraft.From(new MemberTransferDto(3, 1, 55.00m), Day);

        draft.Problems().Should().BeEmpty();
        draft.ToRequest().Should().Be(new SettlementCreateRequest(3, 1, 55.00m, "USD", null, Day));
        draft.AmountUsd.Should().Be(55.00m);
    }

    [Fact]
    public void A_payment_in_another_currency_needs_the_rate_and_counts_in_USD()
    {
        var draft = SettlementDraft.From(new MemberTransferDto(2, 1, 25m), Day);
        draft.Currency = "EUR";
        draft.AmountText = "۲۳٫۰۰";

        draft.Problems().Select(p => p.Key).Should().Equal("ErrorRate");
        draft.PerUsdText = "0.92";

        draft.Problems().Should().BeEmpty();
        draft.ToRequest().Should().Be(new SettlementCreateRequest(2, 1, 23.00m, "EUR", 1.08695652m, Day));
        draft.AmountUsd.Should().Be(25.00m); // 23 x 1.08695652 = 24.99999996, half-up
    }

    [Fact]
    public void Paying_oneself_or_nothing_is_refused()
    {
        var draft = new SettlementDraft { FromMemberId = 1, ToMemberId = 1, AmountText = "0", Date = Day };

        draft.Problems().Select(p => p.Key).Should().Equal("ErrorSettleSame", "ErrorAmount");
        new SettlementDraft { AmountText = "5", Date = Day }.Problems().Select(p => p.Key).Should().Equal("ErrorSettleMembers");
    }
}

/// <summary>How a balance is told: owed (positive), owes (negative) or settled.</summary>
public sealed class BalanceWordingTests
{
    [Theory]
    [InlineData(43.48, "IsOwed", "delato-owed")]
    [InlineData(-0.01, "Owes", "delato-owes")]
    [InlineData(0, "SettledUp", "delato-muted")]
    public void A_balance_is_owed_owes_or_settled(double net, string key, string css)
    {
        MySummary.StateKey((decimal)net).Should().Be(key);
        GroupList.BalanceClass((decimal)net).Should().Be(css);
    }
}

/// <summary>Every form field has an id, and a label names it with <c>for</c>, so assistive technology names each field.</summary>
public sealed class LabelTests
{
    private static IEnumerable<string> Pages() =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory, "client-sources"), "*.razor", SearchOption.AllDirectories);

    [Fact]
    public void Every_input_select_and_file_picker_has_an_id_and_a_label_for_it()
    {
        var fields = 0;
        foreach (var page in Pages())
        {
            var text = File.ReadAllText(page);
            foreach (Match field in Regex.Matches(text, @"<(input|select|InputFile)\b[^>]*>", RegexOptions.Singleline))
            {
                fields++;
                var id = Regex.Match(field.Value, @"\bid=""@Id\((""[^""]+""(?: \+ [^)]+)?)\)""");
                id.Success.Should().BeTrue($"{Path.GetFileName(page)}: {field.Value} has no id");
                text.Should().Contain($"for=\"@Id({id.Groups[1].Value})\"", $"{Path.GetFileName(page)}: nothing labels {id.Groups[1].Value}");
            }
        }
        fields.Should().BeGreaterThan(15);
    }

    [Fact]
    public void Every_part_takes_its_ids_from_the_shared_base()
    {
        foreach (var page in Pages().Where(p => File.ReadAllText(p).Contains("@Id(")))
        {
            File.ReadAllText(page).Should().Contain("@inherits LedgerPart", Path.GetFileName(page));
        }
    }
}
