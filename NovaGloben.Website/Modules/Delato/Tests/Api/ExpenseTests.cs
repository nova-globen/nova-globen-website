using Delato.Server.Services;
using Delato.Shared.Contracts;

namespace Delato.Tests;

/// <summary>Expenses: every split mode, foreign currencies with their rate, edit and delete - the old API's cases.</summary>
public sealed class ExpenseTests
{
    private readonly LedgerHost _host = new();
    private DelatoLedger Ledger => _host.Ledger;
    private const int Site = LedgerHost.Site;
    private static readonly DelatoCaller Alice = LedgerHost.Confirmed(1, "Alice");
    private static readonly DateOnly Day = new(2026, 10, 29);

    private async Task<(int GroupId, int A, int B, int C)> GroupAsync()
    {
        var group = (await Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("Trip"))).Body<GroupDto>(201);
        var a = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single().Id;
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201).Id;
        var c = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Carol", null))).Body<MemberDto>(201).Id;
        return (group.Id, a, b, c);
    }

    private static ExpenseTargetDto M(int id, decimal? value = null) => new("Member", id, value);

    private static ExpenseCreateRequest Body(int payer, decimal amount, string currency, string mode, List<ExpenseTargetDto> targets,
        decimal? fx = null, string? storage = null, string description = "Dinner") =>
        new(description, Day, payer, amount, currency, fx, storage, mode, targets);

    private static decimal ShareOf(ExpenseDto e, int memberId) => e.Shares.Single(s => s.MemberId == memberId).AmountUsd;

    [Fact]
    public async Task A_USD_equal_split_gives_the_leftover_cent_to_the_lowest_member_id()
    {
        var (g, a, b, c) = await GroupAsync();

        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 100m, "USD", "Equal", [M(a), M(b), M(c)]))).Body<ExpenseDto>(201);

        e.FxRateToUsd.Should().Be(1m);
        e.AmountUsd.Should().Be(100.00m);
        e.StorageMode.Should().Be("KeepOriginal");
        ShareOf(e, a).Should().Be(33.34m);
        ShareOf(e, b).Should().Be(33.33m);
        ShareOf(e, c).Should().Be(33.33m);
    }

    [Fact]
    public async Task A_foreign_expense_needs_its_rate()
    {
        var (g, a, b, _) = await GroupAsync();

        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 50m, "EUR", "Equal", [M(a), M(b)], storage: "ConvertedToUsd")))
            .ErrorCode(400).Should().Be("VALIDATION");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task A_foreign_expense_without_a_storage_mode_is_converted_to_USD(string? storage)
    {
        var (g, a, b, _) = await GroupAsync();

        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 100.01m, "EUR", "Equal", [M(a), M(b)], 1.0837m, storage))).Body<ExpenseDto>(201);

        e.StorageMode.Should().Be("ConvertedToUsd");
        e.AmountUsd.Should().Be(108.38m);
        ShareOf(e, a).Should().Be(54.19m);
        ShareOf(e, b).Should().Be(54.19m);
    }

    [Fact]
    public async Task An_unknown_storage_mode_is_still_refused()
    {
        var (g, a, b, _) = await GroupAsync();

        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 10m, "EUR", "Equal", [M(a), M(b)], 1.1m, "Sometimes")))
            .ErrorCode(400).Should().Be("VALIDATION");
    }

    [Fact]
    public async Task A_EUR_expense_computes_its_USD_amount_half_up_in_both_storage_modes()
    {
        var (g, a, b, _) = await GroupAsync();

        var converted = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 120.50m, "EUR", "Equal", [M(a), M(b)], 1.09m, "ConvertedToUsd"))).Body<ExpenseDto>(201);
        var kept = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 120.50m, "EUR", "Equal", [M(a), M(b)], 1.09m, "KeepOriginal"))).Body<ExpenseDto>(201);

        converted.AmountOriginal.Should().Be(120.50m);
        converted.CurrencyCode.Should().Be("EUR");
        converted.AmountUsd.Should().Be(131.35m); // 131.345 -> HALF_UP
        converted.StorageMode.Should().Be("ConvertedToUsd");
        ShareOf(converted, a).Should().Be(65.68m);
        ShareOf(converted, b).Should().Be(65.67m);
        kept.StorageMode.Should().Be("KeepOriginal");
        kept.AmountUsd.Should().Be(131.35m);
    }

    [Fact]
    public async Task Shares_split_in_proportion()
    {
        var (g, a, b, _) = await GroupAsync();

        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 90m, "USD", "Shares", [M(a, 2), M(b, 1)]))).Body<ExpenseDto>(201);

        ShareOf(e, a).Should().Be(60.00m);
        ShareOf(e, b).Should().Be(30.00m);
    }

    [Fact]
    public async Task Percentages_must_make_one_hundred()
    {
        var (g, a, b, _) = await GroupAsync();

        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 50m, "USD", "Percentage", [M(a, 60), M(b, 40)]))).Body<ExpenseDto>(201);
        ShareOf(e, a).Should().Be(30.00m);
        ShareOf(e, b).Should().Be(20.00m);

        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 50m, "USD", "Percentage", [M(a, 60), M(b, 30)]))).ErrorCode(409).Should().Be("SPLIT_MISMATCH");
    }

    [Fact]
    public async Task Exact_amounts_must_make_the_total()
    {
        var (g, a, b, _) = await GroupAsync();

        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 100m, "USD", "ExactAmounts", [M(a, 40.25m), M(b, 59.75m)]))).Body<ExpenseDto>(201);
        ShareOf(e, a).Should().Be(40.25m);
        ShareOf(e, b).Should().Be(59.75m);

        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 100m, "USD", "ExactAmounts", [M(a, 40m), M(b, 50m)]))).ErrorCode(409).Should().Be("SPLIT_MISMATCH");
    }

    [Fact]
    public async Task An_entity_share_divides_equally_among_its_members()
    {
        var (g, a, b, c) = await GroupAsync();
        var couple = (await Ledger.AddEntityAsync(Site, Alice, g, new EntityCreateRequest("B&C", "Couple", [b, c]))).Body<EntityDto>(201);

        // 100.02 -> 50.01 per target, then the couple's 50.01 -> 25.01 / 25.00 (the cent to the lower member id)
        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 100.02m, "USD", "Equal", [M(a), new("Entity", couple.Id, null)]))).Body<ExpenseDto>(201);

        ShareOf(e, a).Should().Be(50.01m);
        ShareOf(e, b).Should().Be(25.01m);
        ShareOf(e, c).Should().Be(25.00m);
    }

    [Fact]
    public async Task Editing_an_expense_recomputes_its_shares()
    {
        var (g, a, b, _) = await GroupAsync();
        var created = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 100m, "USD", "Equal", [M(a), M(b)]))).Body<ExpenseDto>(201);

        var updated = (await Ledger.UpdateExpenseAsync(Site, Alice, g, created.Id,
            Body(b, 200m, "USD", "Shares", [M(a, 3), M(b, 1)], description: "Updated expense"))).Body<ExpenseDto>();

        updated.Id.Should().Be(created.Id);
        updated.Description.Should().Be("Updated expense");
        updated.AmountUsd.Should().Be(200.00m);
        updated.SplitMode.Should().Be("Shares");
        ShareOf(updated, a).Should().Be(150.00m);
        ShareOf(updated, b).Should().Be(50.00m);
        ShareOf((await Ledger.GetExpenseAsync(Site, Alice, g, created.Id)).Body<ExpenseDto>(), a).Should().Be(150.00m);
        (await Ledger.ListExpensesAsync(Site, Alice, g)).Body<List<ExpenseDto>>().Should().ContainSingle();
    }

    [Fact]
    public async Task A_deleted_expense_is_gone()
    {
        var (g, a, _, _) = await GroupAsync();
        var created = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 10m, "USD", "Equal", [M(a)]))).Body<ExpenseDto>(201);

        (await Ledger.DeleteExpenseAsync(Site, Alice, g, created.Id)).Status.Should().Be(204);
        (await Ledger.GetExpenseAsync(Site, Alice, g, created.Id)).Status.Should().Be(404);
        (await Ledger.DeleteExpenseAsync(Site, Alice, g, created.Id)).Status.Should().Be(404);
    }

    [Fact]
    public async Task A_zero_decimal_currency_is_fine_because_only_the_USD_side_is_in_cents()
    {
        var (g, a, b, _) = await GroupAsync();

        var e = (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 5000000m, "IRR", "Equal", [M(a), M(b)], 0.0000017m, "KeepOriginal"))).Body<ExpenseDto>(201);

        e.AmountOriginal.Should().Be(5000000m);
        e.AmountUsd.Should().Be(8.50m);
        ShareOf(e, a).Should().Be(4.25m);
    }

    [Theory]
    [InlineData(0, "USD")]
    [InlineData(10.001, "USD")]
    [InlineData(10, "XXX")]
    public async Task Bad_amounts_and_currencies_are_refused(double amount, string currency)
    {
        var (g, a, _, _) = await GroupAsync();

        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, (decimal)amount, currency, "Equal", [M(a)], 1m, "KeepOriginal")))
            .ErrorCode(400).Should().Be("VALIDATION");
    }

    [Fact]
    public async Task A_payer_or_target_from_outside_the_group_is_refused()
    {
        var (g, a, _, _) = await GroupAsync();

        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(999, 10m, "USD", "Equal", [M(a)]))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 10m, "USD", "Equal", [M(999)]))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddExpenseAsync(Site, Alice, g, Body(a, 10m, "USD", "Equal", [new("Entity", 999, null)]))).ErrorCode(400).Should().Be("VALIDATION");
    }
}
