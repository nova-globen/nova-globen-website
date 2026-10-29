using Delato.Server.Services;
using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Tests;

/// <summary>The whole flow - expenses, report, settle-up, summaries - and settle-up always in USD.</summary>
public sealed class SettlementReportTests
{
    private readonly LedgerHost _host = new();
    private DelatoLedger Ledger => _host.Ledger;
    private const int Site = LedgerHost.Site;
    private static readonly DelatoCaller Alice = LedgerHost.Confirmed(1, "Alice");
    private static readonly DelatoCaller Bob = LedgerHost.Confirmed(2, "Bob");
    private static readonly DateOnly Day = new(2026, 10, 29);

    private static void TransfersSettleExactly(IReadOnlyList<MemberBalanceDto> balances, IReadOnlyList<MemberTransferDto> transfers)
    {
        var nets = balances.ToDictionary(b => b.MemberId, b => b.NetUsd);
        foreach (var t in transfers)
        {
            t.AmountUsd.Should().BePositive();
            nets[t.FromMemberId] += t.AmountUsd;
            nets[t.ToMemberId] -= t.AmountUsd;
        }
        nets.Values.Should().AllSatisfy(v => v.Should().Be(0m));
    }

    private static ExpenseCreateRequest Equal(int payer, decimal amount, params int[] members) =>
        new("e", Day, payer, amount, "USD", null, null, "Equal", members.Select(m => new ExpenseTargetDto("Member", m, null)).ToList());

    [Fact]
    public async Task Full_scenario_report_settlement_and_summaries()
    {
        // Alice (creator) + Bob (added by his address, confirmed, so he claims his member) + Carol; couple Bob & Carol
        var group = (await Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("Ski Trip"))).Body<GroupDto>(201);
        var a = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", Bob.ConfirmedEmail))).Body<MemberDto>(201);
        var c = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Carol", null))).Body<MemberDto>(201);
        var couple = (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("Bob & Carol", "Couple", [b.Id, c.Id]))).Body<EntityDto>(201);

        // E1: Alice pays 120 among all three (40 each); E2: Bob pays 30 between Bob and Carol (15 each)
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, Equal(a.Id, 120m, a.Id, b.Id, c.Id));
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, Equal(b.Id, 30m, b.Id, c.Id));
        // nets: Alice +80, Bob -25, Carol -55

        var report = (await Ledger.ReportAsync(Site, Alice, group.Id)).Body<GroupReportDto>();
        report.BaseCurrency.Should().Be("USD");
        report.TotalSpentUsd.Should().Be(150.00m);
        report.MemberBalances.Sum(m => m.NetUsd).Should().Be(0m);
        report.MemberBalances.Single(m => m.MemberId == a.Id).NetUsd.Should().Be(80.00m);
        report.MemberBalances.Single(m => m.MemberId == b.Id).NetUsd.Should().Be(-25.00m);
        report.MemberBalances.Single(m => m.MemberId == c.Id).NetUsd.Should().Be(-55.00m);

        report.PairwiseDebts.Should().BeEquivalentTo(new[]
        {
            new MemberTransferDto(b.Id, a.Id, 40.00m),
            new MemberTransferDto(c.Id, a.Id, 40.00m),
            new MemberTransferDto(c.Id, b.Id, 15.00m),
        });
        report.SuggestedTransfers.Should().BeEquivalentTo(new[]
        {
            new MemberTransferDto(c.Id, a.Id, 55.00m),
            new MemberTransferDto(b.Id, a.Id, 25.00m),
        });
        TransfersSettleExactly(report.MemberBalances, report.SuggestedTransfers);

        var coupleBalance = report.EntityBalances.Should().ContainSingle().Subject;
        coupleBalance.Should().Be(new EntityBalanceDto(couple.Id, "Bob & Carol", "Couple", -80.00m));
        report.SuggestedEntityTransfers.Should().ContainSingle().Which.Should().Be(new EntityTransferDto(couple.Id, -a.Id, 80.00m));

        var aliceSummary = (await Ledger.MySummaryAsync(Site, Alice)).Body<MySummaryDto>();
        aliceSummary.TotalNetUsd.Should().Be(80.00m);
        var aliceGroup = aliceSummary.Groups.Should().ContainSingle().Subject;
        aliceGroup.IShouldPay.Should().BeEmpty();
        aliceGroup.IShouldReceive.Should().BeEquivalentTo(new[] { new OwedFromDto(c.Id, "Carol", 55.00m), new OwedFromDto(b.Id, "Bob", 25.00m) });

        var bobSummary = (await Ledger.MySummaryAsync(Site, Bob)).Body<MySummaryDto>();
        bobSummary.TotalNetUsd.Should().Be(-25.00m);
        bobSummary.Groups.Single().IShouldPay.Should().ContainSingle().Which.Should().Be(new OwedToDto(a.Id, "Alice", 25.00m));

        // Carol pays Alice 55 USD
        var settlement = (await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(c.Id, a.Id, 55m, "USD", null, Day))).Body<SettlementDto>(201);
        settlement.AmountUsd.Should().Be(55.00m);

        var after = (await Ledger.ReportAsync(Site, Alice, group.Id)).Body<GroupReportDto>();
        after.TotalSpentUsd.Should().Be(150.00m, "settlements are not spending");
        after.MemberBalances.Select(m => (m.MemberId, m.NetUsd)).Should().BeEquivalentTo(new[] { (a.Id, 25.00m), (b.Id, -25.00m), (c.Id, 0m) });
        after.SuggestedTransfers.Should().ContainSingle().Which.Should().Be(new MemberTransferDto(b.Id, a.Id, 25.00m));
        TransfersSettleExactly(after.MemberBalances, after.SuggestedTransfers);
        after.EntityBalances.Single(e => e.EntityId == couple.Id).NetUsd.Should().Be(-25.00m);
        after.SuggestedEntityTransfers.Should().ContainSingle().Which.Should().Be(new EntityTransferDto(couple.Id, -a.Id, 25.00m));

        var aliceAfter = (await Ledger.MySummaryAsync(Site, Alice)).Body<MySummaryDto>();
        aliceAfter.TotalNetUsd.Should().Be(25.00m);
        aliceAfter.Groups.Single().IShouldReceive.Should().ContainSingle().Which.Should().Be(new OwedFromDto(b.Id, "Bob", 25.00m));
    }

    [Fact]
    public async Task Balances_and_settle_up_are_in_USD_whatever_the_base_currency_says()
    {
        var group = (await Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("Abroad"))).Body<GroupDto>(201);
        var a = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);

        // a group whose BaseCurrency says SEK (as an imported one may): display only
        var stored = (await _host.Store.LoadAsync<Group>(Site, DelatoKind.Group, group.Id))!;
        stored.BaseCurrency = "SEK";
        await _host.Store.StoreAsync(Site, DelatoKind.Group, group.Id, 0, stored);

        // 100 SEK at 0.095 = 9.50 USD, split equally; then Bob settles 2.50 in EUR at 1.10 = 2.75 USD
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, new ExpenseCreateRequest("Fika", Day, a.Id, 100m, "SEK", 0.095m, "KeepOriginal", "Equal",
            [new("Member", a.Id, null), new("Member", b.Id, null)]));
        await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(b.Id, a.Id, 2.50m, "EUR", 1.10m, Day));

        var report = (await Ledger.ReportAsync(Site, Alice, group.Id)).Body<GroupReportDto>();

        report.BaseCurrency.Should().Be("SEK");
        report.TotalSpentUsd.Should().Be(9.50m);
        report.MemberBalances.Single(m => m.MemberId == a.Id).NetUsd.Should().Be(2.00m); // 9.50 - 4.75 - 2.75
        report.MemberBalances.Single(m => m.MemberId == b.Id).NetUsd.Should().Be(-2.00m);
        report.SuggestedTransfers.Should().ContainSingle().Which.Should().Be(new MemberTransferDto(b.Id, a.Id, 2.00m));
        (await Ledger.MySummaryAsync(Site, Alice)).Body<MySummaryDto>().TotalNetUsd.Should().Be(2.00m);
    }

    [Fact]
    public async Task A_settlement_needs_two_members_of_the_group_and_a_rate_outside_USD()
    {
        var group = (await Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("G"))).Body<GroupDto>(201);
        var a = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);

        (await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(a.Id, a.Id, 1m, "USD", null, Day))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(a.Id, 999, 1m, "USD", null, Day))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(b.Id, a.Id, 1m, "EUR", null, Day))).ErrorCode(400).Should().Be("VALIDATION");
        var s = (await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(b.Id, a.Id, 1m, "USD", null, Day))).Body<SettlementDto>(201);
        (await Ledger.DeleteSettlementAsync(Site, Alice, group.Id, s.Id)).Status.Should().Be(204);
        (await Ledger.ListSettlementsAsync(Site, Alice, group.Id)).Body<List<SettlementDto>>().Should().BeEmpty();
    }
}
