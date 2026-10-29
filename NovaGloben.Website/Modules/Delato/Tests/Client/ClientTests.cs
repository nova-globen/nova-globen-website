using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Delato.Client.Services;
using Delato.Server.Startup;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stomme.Shared;

namespace Delato.Tests;

/// <summary>Money as people read and type it, in English and Persian.</summary>
public sealed class MoneyTextTests
{
    [Theory]
    [InlineData(1234.5, "USD", "1,234.50 USD")]
    [InlineData(-16.28, "USD", "-16.28 USD")]
    [InlineData(5000000, "IRR", "5,000,000 IRR")]
    [InlineData(1500.4, "IRT", "1,500 IRT")]
    [InlineData(1234, "JPY", "1,234 JPY")]
    [InlineData(0.1, "EUR", "0.10 EUR")]
    public void Amounts_show_with_the_currencys_decimals_and_invariant_digits(double amount, string currency, string shown) =>
        MoneyText.Format((decimal)amount, currency).Should().Be(shown);

    [Theory]
    [InlineData("fa-IR")]
    [InlineData("sv-SE")]
    [InlineData("de-DE")]
    public void The_ui_culture_never_changes_how_money_is_shown(string culture)
    {
        var before = CultureInfo.CurrentCulture;
        var beforeUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = new CultureInfo(culture);
            MoneyText.Format(1234.56m, "USD").Should().Be("1,234.56 USD");
            MoneyText.Format(5000000m, "IRR").Should().Be("5,000,000 IRR");
        }
        finally
        {
            CultureInfo.CurrentCulture = before;
            CultureInfo.CurrentUICulture = beforeUi;
        }
    }

    [Theory]
    [InlineData("120.50", "120.50")]
    [InlineData("۱۲۰٫۵۰", "120.50")]          // Persian digits and the Persian decimal separator
    [InlineData("١٢٠٫٥", "120.5")]           // Arabic-Indic digits
    [InlineData("۱۲۰.۵", "120.5")]
    [InlineData("1,234.56", "1234.56")]
    [InlineData("۵٬۰۰۰٬۰۰۰", "5000000")]     // the Persian thousands separator
    [InlineData(" 42 ", "42")]
    [InlineData("0.0000017", "0.0000017")]
    public void Typed_numbers_in_any_of_the_digit_sets_read_exactly(string typed, string value) =>
        MoneyText.Parse(typed).Should().Be(decimal.Parse(value, CultureInfo.InvariantCulture));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData(null)]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1e3")]
    [InlineData("1.2.3")]
    [InlineData(".5")]
    [InlineData("5.")]
    [InlineData("12abc")]
    [InlineData("۱۲۰٫۵۰٫۱")]
    public void Anything_else_is_refused(string? typed) =>
        MoneyText.Parse(typed).Should().BeNull();

    [Fact]
    public void A_shown_plain_number_reads_back_the_same() =>
        MoneyText.Parse(MoneyText.Plain(1234.5678m)).Should().Be(1234.5678m);
}

/// <summary>The rate as people quote it ("1 USD = ? XXX") and as the API stores it (1 XXX = ? USD).</summary>
public sealed class FxRateTests
{
    [Theory]
    [InlineData("10.5", "0.0952381")]
    [InlineData("0.92", "1.08695652")]
    [InlineData("588235.29", "0.0000017")]
    [InlineData("1", "1")]
    public void A_quoted_price_becomes_the_stored_rate_half_up_to_8_decimals(string perUsd, string rate) =>
        FxRate.FromUsdPrice(decimal.Parse(perUsd, CultureInfo.InvariantCulture)).Should().Be(decimal.Parse(rate, CultureInfo.InvariantCulture));

    [Fact]
    public void Zero_or_less_is_no_rate()
    {
        FxRate.FromUsdPrice(0m).Should().BeNull();
        FxRate.FromUsdPrice(-1m).Should().BeNull();
        FxRate.ToUsdPrice(0m).Should().BeNull();
    }

    [Fact]
    public void A_stored_rate_prefills_the_quoted_price() =>
        FxRate.ToUsdPrice(1.09m).Should().Be(0.917431m);
}

/// <summary>The expense form's rules and the request and preview it makes.</summary>
public sealed class ExpenseDraftTests
{
    private static readonly GroupDetailDto Group = new(1, "Trip", "USD", 11,
        [new(1, 1, "Alice", 11, "alice@example.test", true), new(2, 1, "Bob", null, null, false), new(3, 1, "Carol", null, null, false)],
        [new(1, 1, "Bob & Carol", "Couple", [2, 3])]);

    private static ExpenseDraft Draft() => ExpenseDraft.For(Group, new DateOnly(2026, 10, 29), payer: 1);

    [Fact]
    public void A_new_draft_offers_every_member_and_entity_and_selects_the_members()
    {
        var draft = Draft();

        draft.Targets.Select(t => (t.Type, t.Id, t.Selected)).Should().Equal(
            (TargetType.Member, 1, true), (TargetType.Member, 2, true), (TargetType.Member, 3, true), (TargetType.Entity, 1, false));
        draft.PayerMemberId.Should().Be(1);
    }

    [Fact]
    public void An_empty_draft_names_what_is_missing() =>
        Draft().Problems().Select(p => p.Key).Should().Equal("ErrorDescription", "ErrorAmount");

    [Fact]
    public void A_USD_equal_split_previews_the_servers_shares()
    {
        var draft = Draft();
        draft.Description = "Dinner";
        draft.AmountText = "۱۰۰";

        draft.Problems().Should().BeEmpty();
        var (usd, shares) = draft.Preview()!.Value;
        usd.Should().Be(100m);
        shares.Select(s => s.AmountUsd).Should().Equal(33.34m, 33.33m, 33.33m);
        var request = draft.ToRequest();
        (request.CurrencyCode, request.FxRateToUsd, request.StorageMode, request.SplitMode).Should().Be(("USD", (decimal?)null, (string?)null, "Equal"));
        request.Targets.Should().OnlyContain(t => t.Type == "Member" && t.Value == null);
    }

    [Fact]
    public void A_foreign_amount_needs_the_rate_and_sends_the_inverse_and_the_storage_choice()
    {
        var draft = Draft();
        draft.Description = "Taxi";
        draft.AmountText = "120.50";
        draft.Currency = "EUR";

        draft.Problems().Select(p => p.Key).Should().Equal("ErrorRate");
        draft.PerUsdText = "0.92";
        draft.Storage = StorageMode.KeepOriginal;

        draft.Problems().Should().BeEmpty();
        var request = draft.ToRequest();
        (request.FxRateToUsd, request.StorageMode).Should().Be((1.08695652m, "KeepOriginal"));
        draft.Preview()!.Value.AmountUsd.Should().Be(130.98m); // 120.50 x 1.08695652 = 130.978..., half-up
    }

    [Fact]
    public void Percentages_must_make_one_hundred_and_shares_whole_numbers()
    {
        var draft = Draft();
        draft.Description = "Rent";
        draft.AmountText = "50";
        draft.Mode = SplitMode.Percentage;
        draft.Targets[2].Selected = false;
        draft.Targets[0].ValueText = "60";
        draft.Targets[1].ValueText = "30";

        draft.Problems().Should().ContainSingle().Which.Should().BeEquivalentTo(new DraftProblem("ErrorPercentSum", "90"));
        draft.Targets[1].ValueText = "۴۰";
        draft.Problems().Should().BeEmpty();
        draft.Preview()!.Value.Shares.Select(s => s.AmountUsd).Should().Equal(30m, 20m);

        draft.Mode = SplitMode.Shares;
        draft.Targets[0].ValueText = "1.5";
        draft.Problems().Select(p => p.Key).Should().Equal("ErrorShares");
    }

    [Fact]
    public void Exact_amounts_must_make_the_total()
    {
        var draft = Draft();
        draft.Description = "Groceries";
        draft.AmountText = "100";
        draft.Mode = SplitMode.ExactAmounts;
        draft.Targets[2].Selected = false;
        draft.Targets[0].ValueText = "40.25";
        draft.Targets[1].ValueText = "50";

        draft.Problems().Should().ContainSingle().Which.Should().BeEquivalentTo(new DraftProblem("ErrorExactSum", "90.25", "100"));
        draft.Targets[1].ValueText = "59.75";
        draft.Preview()!.Value.Shares.Select(s => s.AmountUsd).Should().Equal(40.25m, 59.75m);
    }

    [Fact]
    public void An_entity_target_previews_its_members_shares()
    {
        var draft = Draft();
        draft.Description = "Hotel";
        draft.AmountText = "100.02";
        draft.Targets[1].Selected = draft.Targets[2].Selected = false;
        draft.Targets[3].Selected = true;

        draft.Preview()!.Value.Shares.Select(s => (s.MemberId, s.AmountUsd)).Should().Equal((1, 50.01m), (2, 25.01m), (3, 25.00m));
    }

    [Fact]
    public void An_existing_expense_opens_as_it_was_saved()
    {
        var expense = new ExpenseDto(5, 1, "Taxi", new DateOnly(2026, 8, 20), 2, 120.50m, "EUR", 1.09m, 131.35m, "KeepOriginal", "Shares",
            [new("Member", 1, 2m), new("Entity", 1, 1m)], []);

        var draft = ExpenseDraft.From(Group, expense);

        (draft.Description, draft.PayerMemberId, draft.AmountText, draft.Currency, draft.PerUsdText, draft.Storage, draft.Mode)
            .Should().Be(("Taxi", 2, "120.5", "EUR", "0.917431", StorageMode.KeepOriginal, SplitMode.Shares));
        draft.Targets.Where(t => t.Selected).Select(t => (t.Type, t.Id, t.ValueText)).Should().Equal((TargetType.Member, 1, "2"), (TargetType.Entity, 1, "1"));
    }
}

/// <summary>An import's answer, per kind.</summary>
public sealed class ImportSummaryTests
{
    [Fact]
    public void Created_and_skipped_are_counted_per_kind()
    {
        var lines = ImportSummary.Lines(new ImportResult(2, 2, 0, 1, 0, 0, ["group 1", "group 2", "expense 1"]));

        lines.Should().Equal(new ImportLine("Groups", 2, 0, 2), new ImportLine("Expenses", 1, 0, 1), new ImportLine("Settlements", 0, 0, 0));
        ImportSummary.Lines(new ImportResult(2, 2, 0, 1, 0, 3, [])).Select(l => l.Created).Should().Equal(2, 1, 0);
        ImportSummary.MaxBytes.Should().Be(1024 * 1024);
    }
}

/// <summary>Delato's texts: every English key has a Persian text, and the localizer the host builds reads both.</summary>
public sealed class TextTests
{
    private static readonly ResourceManager Resources = new("Delato.Resources.DelatoText", typeof(DelatoText).Assembly);

    private static Dictionary<string, string> Texts(string culture) =>
        Resources.GetResourceSet(culture == "" ? CultureInfo.InvariantCulture : new CultureInfo(culture), true, false)!
            .Cast<System.Collections.DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [Fact]
    public void Every_English_key_has_a_Persian_text()
    {
        var english = Texts("");
        var persian = Texts("fa");

        english.Should().NotBeEmpty();
        persian.Keys.Should().BeEquivalentTo(english.Keys);
        persian.Should().OnlyContain(kv => !string.IsNullOrWhiteSpace(kv.Value));
    }

    [Fact]
    public void Every_text_the_pages_ask_for_exists()
    {
        var english = Texts("");
        var root = Path.Combine(AppContext.BaseDirectory, "client-sources");
        var asked = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), @"T\[""([A-Za-z]+)""\]").Select(m => m.Groups[1].Value))
            .Concat(Regex.Matches(File.ReadAllText(Path.Combine(root, "Services", "ExpenseDraft.cs")), @"new\(""(Error[A-Za-z]+)""").Select(m => m.Groups[1].Value))
            .Concat(Enum.GetNames<SplitMode>().Select(m => "Split" + m))
            .Concat(Enum.GetNames<EntityKind>().Select(k => "Kind" + k))
            .Concat(new[] { "Groups", "Expenses", "Settlements" }.Select(k => "Import" + k))
            .Concat(new[] { 1m, -1m, 0m }.Select(Delato.Client.Ui.MySummary.StateKey))
            .ToHashSet();

        asked.Should().NotBeEmpty();
        asked.Except(english.Keys).Should().BeEmpty("every key a page asks for must be in DelatoText.resx");
    }

    [Theory]
    [InlineData("en", "AddExpense", "Add expense")]
    [InlineData("fa", "AddExpense", "افزودن هزینه")]
    [InlineData("fa-IR", "Groups", "گروه‌ها")]
    public void The_hosts_localizer_reads_the_language_of_the_page(string culture, string key, string text)
    {
        var factory = new ResourceManagerStringLocalizerFactory(Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }), NullLoggerFactory.Instance);
        var localizer = new StringLocalizer<DelatoText>(factory);
        var before = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(culture);
            localizer[key].Value.Should().Be(text);
            localizer[key].ResourceNotFound.Should().BeFalse();
        }
        finally
        {
            CultureInfo.CurrentUICulture = before;
        }
    }

    [Fact]
    public void Persian_is_written_right_to_left() =>
        new CultureInfo("fa").TextInfo.IsRightToLeft.Should().BeTrue();
}

/// <summary>Home carries the ledger for signed-in accounts only.</summary>
public sealed class HomeTests
{
    [Fact]
    public void Home_is_for_signed_in_accounts_and_the_sites_administrators()
    {
        DelatoSiteReconciler.SignedInOnly().Select(p => (p.PermissionName, p.RoleName, p.IsAuthorized)).Should().BeEquivalentTo(new[]
        {
            (PermissionNames.View, RoleNames.Registered, true),
            (PermissionNames.View, RoleNames.Admin, true),
            (PermissionNames.Edit, RoleNames.Admin, true),
        });
        DelatoSiteReconciler.SignedInOnly().Should().NotContain(p => p.RoleName == RoleNames.Everyone);
    }

    [Fact]
    public void The_ledger_module_is_named_as_Stomme_registers_it() =>
        DelatoSiteReconciler.LedgerModule.Should().StartWith("Delato.Module.Ledger").And.EndWith("Delato.Client.Stomme");
}
