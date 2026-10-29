using Delato.Server.Services;
using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Tests;

/// <summary>
/// Every money path goes JSON -> store -> back and keeps every digit: amounts, rates, targets' values, shares and
/// settlements are decimal end to end, never a binary floating point.
/// </summary>
public sealed class MoneyRoundTripTests
{
    private readonly LedgerHost _host = new();
    private static readonly DelatoCaller Alice = LedgerHost.Confirmed(1, "Alice");

    [Fact]
    public async Task Decimals_written_as_JSON_numbers_read_back_exactly()
    {
        var tenant = _host.Tenants.CurrentTenantId!;
        await _host.Rows.AddAsync(DelatoDocument.Create(Guid.NewGuid(), tenant, LedgerHost.Site, DelatoKind.Settlement, 1, 1,
            "{\"id\":1,\"groupId\":1,\"currencyCode\":\"EUR\",\"amountOriginal\":0.1,\"fxRateToUsd\":0.2,\"amountUsd\":123456789012.34}",
            DateTime.UtcNow));

        var read = (await _host.Store.LoadAsync<Settlement>(LedgerHost.Site, DelatoKind.Settlement, 1, 1))!;

        (read.AmountOriginal + read.FxRateToUsd).Should().Be(0.3m);
        read.AmountUsd.Should().Be(123456789012.34m);
    }

    [Fact]
    public async Task An_expense_document_keeps_every_money_field()
    {
        var expense = new Expense
        {
            Id = 4, GroupId = 2, Description = "a", CurrencyCode = "EUR",
            AmountOriginal = 0.1m + 0.2m, FxRateToUsd = 1.23456789m, AmountUsd = 123456789012.34m,
            Targets = [new ExpenseTarget { Id = 1, TargetId = 1, Value = 33.33m }, new ExpenseTarget { Id = 2, TargetId = 2, Value = 66.67m }],
            Shares = [new ExpenseShare { Id = 1, MemberId = 1, AmountUsd = 41152263004.12m }, new ExpenseShare { Id = 2, MemberId = 2, AmountUsd = 0.01m }],
        };

        await _host.Store.StoreAsync(LedgerHost.Site, DelatoKind.Expense, 2, 4, expense);
        var read = (await _host.Store.LoadAsync<Expense>(LedgerHost.Site, DelatoKind.Expense, 2, 4))!;

        read.AmountOriginal.Should().Be(0.3m);
        read.FxRateToUsd.Should().Be(1.23456789m);
        read.AmountUsd.Should().Be(123456789012.34m);
        read.Targets.Select(t => t.Value).Should().Equal(33.33m, 66.67m);
        read.Shares.Select(s => s.AmountUsd).Should().Equal(41152263004.12m, 0.01m);
    }

    [Fact]
    public async Task A_settlement_document_keeps_every_money_field()
    {
        var settlement = new Settlement
        {
            Id = 3, GroupId = 2, CurrencyCode = "IRR",
            AmountOriginal = 999999999999.99m, FxRateToUsd = 0.00000171m, AmountUsd = 1710000.00m,
        };

        await _host.Store.StoreAsync(LedgerHost.Site, DelatoKind.Settlement, 2, 3, settlement);
        var read = (await _host.Store.LoadAsync<Settlement>(LedgerHost.Site, DelatoKind.Settlement, 2, 3))!;

        read.AmountOriginal.Should().Be(999999999999.99m);
        read.FxRateToUsd.Should().Be(0.00000171m);
        read.AmountUsd.Should().Be(1710000.00m);
    }

    [Fact]
    public async Task A_one_third_split_through_the_ledger_sums_to_the_total_after_the_store()
    {
        var (groupId, a, b, c) = await ThreeMembersAsync();

        var created = (await _host.Ledger.AddExpenseAsync(LedgerHost.Site, Alice, groupId,
            Expense(a, 10.00m, "USD", null, "Equal", a, b, c))).Body<ExpenseDto>(201);
        var read = (await _host.Ledger.GetExpenseAsync(LedgerHost.Site, Alice, groupId, created.Id)).Body<ExpenseDto>();

        read.Shares.Select(s => s.AmountUsd).Should().Equal(3.34m, 3.33m, 3.33m);
        read.Shares.Sum(s => s.AmountUsd).Should().Be(10.00m);
    }

    [Fact]
    public async Task A_twelve_digit_amount_splits_and_reads_back_to_the_cent()
    {
        var (groupId, a, b, c) = await ThreeMembersAsync();

        var created = (await _host.Ledger.AddExpenseAsync(LedgerHost.Site, Alice, groupId,
            Expense(a, 123456789012.34m, "USD", null, "Equal", a, b, c))).Body<ExpenseDto>(201);
        var read = (await _host.Ledger.GetExpenseAsync(LedgerHost.Site, Alice, groupId, created.Id)).Body<ExpenseDto>();

        read.AmountOriginal.Should().Be(123456789012.34m);
        read.AmountUsd.Should().Be(123456789012.34m);
        // 12345678901234 cents / 3: two shares of ...411 and the leftover cent to the lowest member id
        read.Shares.Select(s => s.AmountUsd).Should().Equal(41152263004.12m, 41152263004.11m, 41152263004.11m);
        read.Shares.Sum(s => s.AmountUsd).Should().Be(123456789012.34m);
    }

    [Fact]
    public async Task A_foreign_amount_keeps_its_original_and_its_rate()
    {
        var (groupId, a, b, _) = await ThreeMembersAsync();

        var created = (await _host.Ledger.AddExpenseAsync(LedgerHost.Site, Alice, groupId,
            Expense(a, 0.10m, "EUR", 1.08765432m, "Equal", a, b) with { StorageMode = "KeepOriginal" })).Body<ExpenseDto>(201);
        var read = (await _host.Ledger.GetExpenseAsync(LedgerHost.Site, Alice, groupId, created.Id)).Body<ExpenseDto>();

        read.AmountOriginal.Should().Be(0.10m);
        read.FxRateToUsd.Should().Be(1.08765432m);
        read.AmountUsd.Should().Be(0.11m);
        read.Shares.Select(s => s.AmountUsd).Should().Equal(0.06m, 0.05m);
    }

    [Fact]
    public async Task A_settlement_through_the_ledger_reads_back_exactly()
    {
        var (groupId, a, b, _) = await ThreeMembersAsync();

        (await _host.Ledger.AddSettlementAsync(LedgerHost.Site, Alice, groupId,
            new SettlementCreateRequest(b, a, 0.30m, "USD", null, new DateOnly(2026, 10, 29)))).Status.Should().Be(201);
        var list = (await _host.Ledger.ListSettlementsAsync(LedgerHost.Site, Alice, groupId)).Body<List<SettlementDto>>();

        list.Should().ContainSingle().Which.AmountUsd.Should().Be(0.1m + 0.2m);
    }

    private async Task<(int GroupId, int A, int B, int C)> ThreeMembersAsync()
    {
        var group = (await _host.Ledger.CreateGroupAsync(LedgerHost.Site, Alice, new GroupCreateRequest("Trip"))).Body<GroupDto>(201);
        var detail = (await _host.Ledger.GetGroupAsync(LedgerHost.Site, Alice, group.Id)).Body<GroupDetailDto>();
        var b = (await _host.Ledger.AddMemberAsync(LedgerHost.Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);
        var c = (await _host.Ledger.AddMemberAsync(LedgerHost.Site, Alice, group.Id, new MemberCreateRequest("Carol", null))).Body<MemberDto>(201);
        return (group.Id, detail.Members.Single().Id, b.Id, c.Id);
    }

    internal static ExpenseCreateRequest Expense(int payer, decimal amount, string currency, decimal? fx, string mode, params int[] members) =>
        new("test", new DateOnly(2026, 10, 29), payer, amount, currency, fx, currency == "USD" ? null : "ConvertedToUsd", mode,
            members.Select(m => new ExpenseTargetDto("Member", m, null)).ToList());
}
