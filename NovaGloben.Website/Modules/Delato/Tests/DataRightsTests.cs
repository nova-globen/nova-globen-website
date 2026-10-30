using Delato.Server.Services;
using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;
using Stomme.Erasure;

namespace Delato.Tests;

/// <summary>A person deleted from the Delato site: unnamed and unlinked in their groups, whose shared ledgers stay.</summary>
public sealed class DataRightsTests
{
    private readonly LedgerHost _host = new();

    [Fact]
    public async Task Deleting_a_person_unnames_and_unlinks_their_memberships_and_keeps_the_groups()
    {
        await _host.Store.StoreAsync(LedgerHost.Site, DelatoKind.Group, 1, 0, new Group
        {
            Id = 1, Name = "Trip", CreatedByUserId = 5,
            Members = [new Member { Id = 1, GroupId = 1, Name = "Ada", UserId = 5, Email = "ada@example.test" }, new Member { Id = 2, GroupId = 1, Name = "Bo", UserId = 6, Email = "bo@example.test" }],
        });
        await _host.Store.StoreAsync(LedgerHost.Site, DelatoKind.Group, 2, 0, new Group { Id = 2, Name = "Flat", CreatedByUserId = 6, Members = [new Member { Id = 3, GroupId = 2, Name = "Bo", UserId = 6 }] });
        var erasure = new DelatoUserErasure(_host.Store);

        var outcome = (await erasure.EraseAsync(new ErasureSubject(LedgerHost.Site, 5, null, "ada"))).Single();

        (outcome.Store, outcome.Handling, outcome.Affected).Should().Be((DelatoUserErasure.GroupsStore, ErasureHandling.Anonymise, 1));
        var trip = (await _host.Store.LoadAsync<Group>(LedgerHost.Site, DelatoKind.Group, 1))!;
        trip.CreatedByUserId.Should().Be(0);
        trip.Members.Select(m => (m.Name, m.UserId, m.Email)).Should().Equal((DelatoUserErasure.ErasedName, (int?)null, (string?)null), ("Bo", 6, "bo@example.test"));
        (await _host.Store.LoadAsync<Group>(LedgerHost.Site, DelatoKind.Group, 2))!.CreatedByUserId.Should().Be(6, "another person's group is untouched");

        (await erasure.EraseAsync(new ErasureSubject(LedgerHost.Site, 5, null, "ada"))).Single().Affected.Should().Be(0, "a second run finds nothing");
    }

    [Fact]
    public async Task Forgetting_an_address_unnames_only_the_members_no_account_has_claimed()
    {
        await _host.Store.StoreAsync(LedgerHost.Site, DelatoKind.Group, 1, 0, new Group
        {
            Id = 1, Name = "Trip", CreatedByUserId = 6,
            Members = [new Member { Id = 1, GroupId = 1, Name = "Cy", Email = "cy@example.test" }, new Member { Id = 2, GroupId = 1, Name = "Bo", UserId = 6, Email = "bo@example.test" }],
        });
        await _host.Store.StoreAsync(LedgerHost.Site, DelatoKind.Group, 2, 0, new Group
        {
            Id = 2, Name = "Flat", CreatedByUserId = 6,
            Members = [new Member { Id = 3, GroupId = 2, Name = "Cyrus", Email = "CY@example.test" }, new Member { Id = 4, GroupId = 2, Name = "Claimed", UserId = 7, Email = "cy@example.test" }],
        });
        var forget = new DelatoForget(_host.Store);

        (await forget.ForgetEmailAsync(LedgerHost.Site, " Cy@Example.test ")).Should().Be(new ForgetResult(2, 2));

        var trip = (await _host.Store.LoadAsync<Group>(LedgerHost.Site, DelatoKind.Group, 1))!;
        var flat = (await _host.Store.LoadAsync<Group>(LedgerHost.Site, DelatoKind.Group, 2))!;
        trip.Members.Select(m => (m.Name, m.Email)).Should().Equal((DelatoUserErasure.ErasedName, (string?)null), ("Bo", "bo@example.test"));
        flat.Members.Select(m => (m.Name, m.UserId, m.Email)).Should().Equal((DelatoUserErasure.ErasedName, (int?)null, (string?)null), ("Claimed", 7, "cy@example.test"));

        (await forget.ForgetEmailAsync(LedgerHost.Site, "cy@example.test")).Should().Be(new ForgetResult(0, 0), "a second run finds nothing");
        (await forget.ForgetEmailAsync(LedgerHost.Site, "  ")).Should().Be(new ForgetResult(0, 0));
    }
}