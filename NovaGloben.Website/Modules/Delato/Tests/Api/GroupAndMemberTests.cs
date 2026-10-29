using Delato.Server.Services;
using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Tests;

/// <summary>Groups, members, couples and subgroups: the rules of the old API, kept.</summary>
public sealed class GroupAndMemberTests
{
    private readonly LedgerHost _host = new();
    private DelatoLedger Ledger => _host.Ledger;
    private const int Site = LedgerHost.Site;
    private static readonly DelatoCaller Alice = LedgerHost.Confirmed(1, "Alice");
    private static readonly DelatoCaller Bob = LedgerHost.Confirmed(2, "Bob");
    private static readonly DelatoCaller Stranger = LedgerHost.Confirmed(9, "Stranger");

    private async Task<GroupDto> GroupAsync(DelatoCaller owner, string name = "Trip") =>
        (await Ledger.CreateGroupAsync(Site, owner, new GroupCreateRequest(name))).Body<GroupDto>(201);

    [Fact]
    public async Task Creating_a_group_adds_its_creator_as_its_first_member()
    {
        var group = await GroupAsync(Alice, "  Ski Trip ");

        group.Name.Should().Be("Ski Trip");
        group.BaseCurrency.Should().Be("USD");
        group.CreatedByUserId.Should().Be(1);
        var detail = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>();
        var me = detail.Members.Should().ContainSingle().Subject;
        me.Name.Should().Be("Alice");
        me.UserId.Should().Be(1);
        me.IsMe.Should().BeTrue();
    }

    [Fact]
    public async Task A_group_needs_a_name() =>
        (await Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest(" "))).ErrorCode(400).Should().Be("VALIDATION");

    [Fact]
    public async Task The_list_carries_member_count_and_my_net_balance()
    {
        var group = await GroupAsync(Alice);
        var me = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var bob = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, MoneyRoundTripTests.Expense(me.Id, 50m, "USD", null, "Equal", me.Id, bob.Id));

        var list = (await Ledger.ListGroupsAsync(Site, Alice)).Body<List<GroupSummaryDto>>();

        var row = list.Should().ContainSingle().Subject;
        row.MemberCount.Should().Be(2);
        row.MyNetBalanceUsd.Should().Be(25.00m);
    }

    [Fact]
    public async Task A_group_can_be_renamed()
    {
        var group = await GroupAsync(Alice);

        (await Ledger.RenameGroupAsync(Site, Alice, group.Id, new GroupCreateRequest("Renamed"))).Body<GroupDto>().Name.Should().Be("Renamed");
    }

    [Fact]
    public async Task Only_its_creator_deletes_a_group_and_its_history_goes_with_it()
    {
        var group = await GroupAsync(Alice);
        await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", Bob.ConfirmedEmail));
        var me = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.First();
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, MoneyRoundTripTests.Expense(me.Id, 10m, "USD", null, "Equal", me.Id));

        (await Ledger.DeleteGroupAsync(Site, Bob, group.Id)).ErrorCode(403).Should().Be("FORBIDDEN");
        (await Ledger.DeleteGroupAsync(Site, Alice, group.Id)).Status.Should().Be(204);

        (await Ledger.GetGroupAsync(Site, Alice, group.Id)).ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
        _host.Rows.Rows.Where(r => r.Kind != DelatoKind.Ids).Should().BeEmpty();
    }

    [Fact]
    public async Task A_stranger_gets_the_same_404_as_for_a_group_that_does_not_exist()
    {
        var group = await GroupAsync(Alice);

        var someoneElses = await Ledger.GetGroupAsync(Site, Stranger, group.Id);
        var missing = await Ledger.GetGroupAsync(Site, Stranger, group.Id + 1);

        someoneElses.ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
        someoneElses.Should().BeEquivalentTo(missing, "ids are sequential, so a different answer would let a stranger count groups");
        foreach (var answer in new[]
        {
            await Ledger.ListMembersAsync(Site, Stranger, group.Id),
            await Ledger.ReportAsync(Site, Stranger, group.Id),
            await Ledger.ListExpensesAsync(Site, Stranger, group.Id),
            await Ledger.AddMemberAsync(Site, Stranger, group.Id, new MemberCreateRequest("X", null)),
            await Ledger.DeleteGroupAsync(Site, Stranger, group.Id),
        })
        {
            answer.Should().BeEquivalentTo(missing);
        }
        (await Ledger.ListGroupsAsync(Site, Stranger)).Body<List<GroupSummaryDto>>().Should().BeEmpty();
    }

    [Fact]
    public async Task Another_site_of_the_host_sees_none_of_the_groups()
    {
        var group = await GroupAsync(Alice);

        (await Ledger.GetGroupAsync(Site + 1, Alice, group.Id)).ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
        (await Ledger.ListGroupsAsync(Site + 1, Alice)).Body<List<GroupSummaryDto>>().Should().BeEmpty();
    }

    [Fact]
    public async Task A_member_is_added_with_or_without_an_email_and_an_email_only_once()
    {
        var group = await GroupAsync(Alice);

        var plain = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Carol", null))).Body<MemberDto>(201);
        var mailed = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", " Bob@Example.TEST "))).Body<MemberDto>(201);

        plain.Email.Should().BeNull();
        mailed.Email.Should().Be("bob@example.test");
        mailed.UserId.Should().BeNull("an address is never claimed by adding it");
        (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob again", "bob@example.test"))).ErrorCode(409).Should().Be("CONFLICT");
    }

    [Fact]
    public async Task A_member_with_a_balance_or_a_history_stays_and_one_without_goes()
    {
        var group = await GroupAsync(Alice);
        var me = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var bob = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);
        var carol = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Carol", null))).Body<MemberDto>(201);
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, MoneyRoundTripTests.Expense(me.Id, 20m, "USD", null, "Equal", me.Id, bob.Id));

        (await Ledger.DeleteMemberAsync(Site, Alice, group.Id, bob.Id)).ErrorCode(409).Should().Be("MEMBER_HAS_BALANCE");
        await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(bob.Id, me.Id, 10m, "USD", null, new DateOnly(2026, 10, 29)));
        (await Ledger.DeleteMemberAsync(Site, Alice, group.Id, bob.Id)).ErrorCode(409).Should().Be("MEMBER_HAS_HISTORY");
        (await Ledger.DeleteMemberAsync(Site, Alice, group.Id, carol.Id)).Status.Should().Be(204);
        (await Ledger.DeleteMemberAsync(Site, Alice, group.Id, carol.Id)).Status.Should().Be(404);
    }

    [Fact]
    public async Task Couples_have_two_members_subgroups_two_or_more_and_a_member_one_couple()
    {
        var group = await GroupAsync(Alice);
        var me = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);
        var c = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Carol", null))).Body<MemberDto>(201);

        (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("Three", "Couple", [me.Id, b.Id, c.Id]))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("One", "Subgroup", [b.Id]))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("Odd", "Club", [b.Id, c.Id]))).ErrorCode(400).Should().Be("VALIDATION");
        (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("Outsider", "Subgroup", [b.Id, 999]))).ErrorCode(400).Should().Be("VALIDATION");

        var couple = (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("B & C", "Couple", [c.Id, b.Id]))).Body<EntityDto>(201);
        couple.MemberIds.Should().Equal(b.Id, c.Id);
        (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("A & B", "Couple", [me.Id, b.Id]))).ErrorCode(409).Should().Be("CONFLICT");
        (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("All", "Subgroup", [me.Id, b.Id, c.Id]))).Status.Should().Be(201);
        (await Ledger.ListEntitiesAsync(Site, Alice, group.Id)).Body<List<EntityDto>>().Should().HaveCount(2);
    }

    [Fact]
    public async Task An_entity_used_by_an_expense_stays()
    {
        var group = await GroupAsync(Alice);
        var me = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);
        var pair = (await Ledger.AddEntityAsync(Site, Alice, group.Id, new EntityCreateRequest("Pair", "Couple", [me.Id, b.Id]))).Body<EntityDto>(201);
        await Ledger.AddExpenseAsync(Site, Alice, group.Id, new ExpenseCreateRequest("x", new DateOnly(2026, 10, 29), me.Id, 10m, "USD", null, null, "Equal",
            [new ExpenseTargetDto("Entity", pair.Id, null)]));

        (await Ledger.DeleteEntityAsync(Site, Alice, group.Id, pair.Id)).ErrorCode(409).Should().Be("CONFLICT");
    }

    [Fact]
    public async Task Created_times_come_from_the_injected_clock()
    {
        _host.Clock.Now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var group = await GroupAsync(Alice);
        var me = (await Ledger.GetGroupAsync(Site, Alice, group.Id)).Body<GroupDetailDto>().Members.Single();
        var expense = (await Ledger.AddExpenseAsync(Site, Alice, group.Id, MoneyRoundTripTests.Expense(me.Id, 1m, "USD", null, "Equal", me.Id))).Body<ExpenseDto>(201);
        var b = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", null))).Body<MemberDto>(201);
        var settlement = (await Ledger.AddSettlementAsync(Site, Alice, group.Id, new SettlementCreateRequest(b.Id, me.Id, 1m, "USD", null, new DateOnly(2026, 1, 2)))).Body<SettlementDto>(201);

        var at = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        (await _host.Store.LoadAsync<Group>(Site, DelatoKind.Group, group.Id))!.CreatedAt.Should().Be(at);
        (await _host.Store.LoadAsync<Expense>(Site, DelatoKind.Expense, group.Id, expense.Id))!.CreatedAt.Should().Be(at);
        (await _host.Store.LoadAsync<Settlement>(Site, DelatoKind.Settlement, group.Id, settlement.Id))!.CreatedAt.Should().Be(at);
    }
}
