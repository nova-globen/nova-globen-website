using Delato.Server.Services;
using Delato.Shared.Contracts;

namespace Delato.Tests;

/// <summary>
/// Linking an account to a member by e-mail happens only for a CONFIRMED e-mail, and a member's e-mail is shown only
/// to the group's own members.
/// </summary>
public sealed class LinkingTests
{
    private readonly LedgerHost _host = new();
    private DelatoLedger Ledger => _host.Ledger;
    private const int Site = LedgerHost.Site;
    private static readonly DelatoCaller Alice = LedgerHost.Confirmed(1, "Alice");

    private async Task<(int GroupId, MemberDto Bob)> GroupWithBobByEmailAsync()
    {
        var group = (await Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("Trip"))).Body<GroupDto>(201);
        var bob = (await Ledger.AddMemberAsync(Site, Alice, group.Id, new MemberCreateRequest("Bob", "bob@example.test"))).Body<MemberDto>(201);
        return (group.Id, bob);
    }

    [Fact]
    public async Task An_account_with_that_email_confirmed_sees_the_group_and_claims_the_member()
    {
        var (groupId, bob) = await GroupWithBobByEmailAsync();
        var bobAccount = new DelatoCaller(2, "Bob", "bob@example.test");

        var detail = (await Ledger.GetGroupAsync(Site, bobAccount, groupId)).Body<GroupDetailDto>();

        var member = detail.Members.Single(m => m.Id == bob.Id);
        member.UserId.Should().Be(2);
        member.IsMe.Should().BeTrue();
        (await Ledger.ListGroupsAsync(Site, bobAccount)).Body<List<GroupSummaryDto>>().Should().ContainSingle();
    }

    [Fact]
    public async Task An_account_with_that_email_unconfirmed_sees_nothing_and_claims_nothing()
    {
        var (groupId, bob) = await GroupWithBobByEmailAsync();
        // registered with Bob's address, never confirmed it: Delato does not see the address at all
        var squatter = LedgerHost.Unconfirmed(3, "Not Bob");

        (await Ledger.GetGroupAsync(Site, squatter, groupId)).ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
        (await Ledger.ListGroupsAsync(Site, squatter)).Body<List<GroupSummaryDto>>().Should().BeEmpty();
        (await Ledger.MySummaryAsync(Site, squatter)).Body<MySummaryDto>().Groups.Should().BeEmpty();

        var member = (await Ledger.GetGroupAsync(Site, Alice, groupId)).Body<GroupDetailDto>().Members.Single(m => m.Id == bob.Id);
        member.UserId.Should().BeNull();
    }

    [Fact]
    public async Task A_claimed_member_is_not_claimed_again_by_another_account()
    {
        var (groupId, bob) = await GroupWithBobByEmailAsync();
        await Ledger.GetGroupAsync(Site, new DelatoCaller(2, "Bob", "bob@example.test"), groupId);

        // a second account that later confirms the same address (after an address change) finds the member taken
        var other = new DelatoCaller(4, "Other", "bob@example.test");

        (await Ledger.GetGroupAsync(Site, other, groupId)).ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
        (await Ledger.GetGroupAsync(Site, Alice, groupId)).Body<GroupDetailDto>().Members.Single(m => m.Id == bob.Id).UserId.Should().Be(2);
    }

    [Fact]
    public async Task A_creator_without_a_confirmed_email_is_not_linked_by_id_but_still_reaches_the_group()
    {
        var unconfirmed = LedgerHost.Unconfirmed(5, "Dana");

        var group = (await Ledger.CreateGroupAsync(Site, unconfirmed, new GroupCreateRequest("Mine"))).Body<GroupDto>(201);
        var detail = (await Ledger.GetGroupAsync(Site, unconfirmed, group.Id)).Body<GroupDetailDto>();

        detail.Members.Single().UserId.Should().BeNull();
        detail.Members.Single().Email.Should().BeNull();
    }

    [Fact]
    public async Task No_member_email_reaches_anyone_outside_the_group()
    {
        var (groupId, _) = await GroupWithBobByEmailAsync();
        var outsider = LedgerHost.Confirmed(8, "Eve");

        var answers = new[]
        {
            await Ledger.GetGroupAsync(Site, outsider, groupId),
            await Ledger.ListMembersAsync(Site, outsider, groupId),
            await Ledger.ReportAsync(Site, outsider, groupId),
            await Ledger.ListGroupsAsync(Site, outsider),
            await Ledger.MySummaryAsync(Site, outsider),
        };

        foreach (var answer in answers)
        {
            System.Text.Json.JsonSerializer.Serialize(answer.Body).Should().NotContain("@example.test");
        }
        // and the summary, even for a member, names people without their address
        var bobAccount = new DelatoCaller(2, "Bob", "bob@example.test");
        System.Text.Json.JsonSerializer.Serialize((await Ledger.MySummaryAsync(Site, bobAccount)).Body)
            .Should().NotContain("alice@example.test");
    }
}
