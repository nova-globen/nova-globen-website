using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Delato.Server.Controllers;
using Delato.Server.Services;
using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;
using Microsoft.AspNetCore.Authorization;
using Stomme.SharedKernel.Domain;
using Stomme.Shared;

namespace Delato.Tests;

/// <summary>
/// The import of an earlier ledger. The fixture (Import/import-fixture.json) has the shape of the real export: the
/// export tool made it from a database that the earlier app filled through its own API. Every value in it is invented.
/// The expected balances below are what the earlier app's own report answered for that database.
/// </summary>
public sealed class ImportTests
{
    private readonly LedgerHost _host = new();
    private DelatoImport Import => new(_host.Store);
    private const int Site = LedgerHost.Site;
    private static readonly DelatoCaller Alice = LedgerHost.Confirmed(11, "Alice");
    private static readonly DelatoCaller Bob = LedgerHost.Confirmed(12, "Bob");
    private const int Records = 2 + 7 + 2;

    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private static string FixtureText() => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Import", "import-fixture.json"));

    private static ImportRequest Fixture() => JsonSerializer.Deserialize<ImportRequest>(FixtureText(), Web)!;

    private static ImportRequest Mutated(Action<JsonNode> change)
    {
        var node = JsonNode.Parse(FixtureText())!;
        change(node);
        return node.Deserialize<ImportRequest>(Web)!;
    }

    private static string Message(Outcome outcome) => ((ErrorEnvelope)outcome.Body!).Error.Message;

    [Fact]
    public async Task The_fixture_imports_and_reports_exactly_what_the_earlier_app_reported()
    {
        var result = (await Import.ImportAsync(Site, Fixture())).Body<ImportResult>();

        result.Should().BeEquivalentTo(new { Groups = 2, Members = 6, Entities = 2, Expenses = 7, Settlements = 2, Created = Records });
        result.Skipped.Should().BeEmpty();
        var report = (await _host.Ledger.ReportAsync(Site, Alice, 1)).Body<GroupReportDto>();
        report.TotalSpentUsd.Should().Be(392.70m);
        report.MemberBalances.Select(m => (m.MemberId, m.NetUsd)).Should().Equal((1, -16.28m), (2, 45.97m), (3, -70.91m), (4, 41.22m));
        report.EntityBalances.Select(e => (e.EntityId, e.NetUsd)).Should().Equal((1, -24.94m), (2, 70.91m));
        report.SuggestedTransfers.Select(t => (t.FromMemberId, t.ToMemberId, t.AmountUsd)).Should().Equal((3, 2, 45.97m), (3, 4, 24.94m), (1, 4, 16.28m));
        report.SuggestedEntityTransfers.Select(t => (t.FromEntityId, t.ToEntityId, t.AmountUsd)).Should().Equal((1, 2, 24.94m));
        report.PairwiseDebts.Select(t => (t.FromMemberId, t.ToMemberId, t.AmountUsd))
            .Should().Equal((1, 4, 36.67m), (2, 1, 9.23m), (2, 4, 4.55m), (3, 1, 11.16m), (3, 2, 59.75m));

        var flat = (await _host.Ledger.ReportAsync(Site, Bob, 2)).Body<GroupReportDto>();
        flat.TotalSpentUsd.Should().Be(1500m);
        flat.MemberBalances.Select(m => (m.MemberId, m.NetUsd)).Should().Equal((5, 750m), (6, -750m));

        (await _host.Ledger.MySummaryAsync(Site, Alice)).Body<MySummaryDto>().TotalNetUsd.Should().Be(-16.28m);
        (await _host.Ledger.MySummaryAsync(Site, Bob)).Body<MySummaryDto>().TotalNetUsd.Should().Be(795.97m);
    }

    [Fact]
    public async Task Every_amount_and_rate_arrives_to_the_last_digit()
    {
        await Import.ImportAsync(Site, Fixture());

        var irr = (await _host.Store.LoadAsync<Expense>(Site, DelatoKind.Expense, 1, 3))!;
        irr.CurrencyCode.Should().Be("IRR");
        irr.AmountOriginal.Should().Be(5000000m);
        irr.FxRateToUsd.Should().Be(0.0000017m);
        irr.AmountUsd.Should().Be(8.50m);
        irr.Shares.Select(s => s.AmountUsd).Should().Equal(4.26m, 2.12m, 2.12m);
        var eur = (await _host.Store.LoadAsync<Settlement>(Site, DelatoKind.Settlement, 1, 2))!;
        (eur.AmountOriginal, eur.FxRateToUsd, eur.AmountUsd).Should().Be((10m, 1.1m, 11m));
    }

    [Fact]
    public async Task A_second_run_creates_nothing_and_reports_every_record_skipped()
    {
        await Import.ImportAsync(Site, Fixture());
        var before = _host.Rows.Rows.Select(r => (r.Kind, r.Number, r.Owner, r.Json, r.UpdatedUtc)).ToList();
        _host.Clock.Now = _host.Clock.Now.AddHours(1);

        var again = (await Import.ImportAsync(Site, Fixture())).Body<ImportResult>();

        again.Created.Should().Be(0);
        again.Skipped.Should().HaveCount(Records).And.Contain(["group 1", "group 2", "expense 8", "settlement 2"]);
        _host.Rows.Rows.Select(r => (r.Kind, r.Number, r.Owner, r.Json, r.UpdatedUtc)).Should().Equal(before);
    }

    [Fact]
    public async Task A_re_import_never_undoes_what_members_changed_since()
    {
        await Import.ImportAsync(Site, Fixture());
        await _host.Ledger.RenameGroupAsync(Site, Alice, 1, new GroupCreateRequest("Renamed since"));
        await _host.Ledger.DeleteExpenseAsync(Site, Alice, 1, 6);

        var again = (await Import.ImportAsync(Site, Fixture())).Body<ImportResult>();

        (await _host.Ledger.GetGroupAsync(Site, Alice, 1)).Body<GroupDetailDto>().Name.Should().Be("Renamed since");
        again.Created.Should().Be(1, "only the expense deleted since is absent, so it comes back");
        again.Skipped.Should().Contain("group 1");
    }

    [Fact]
    public async Task A_later_payload_adds_its_new_records_to_imported_groups()
    {
        await Import.ImportAsync(Site, Fixture());
        var later = Mutated(n =>
        {
            n["settlements"]!.AsArray().Add(JsonNode.Parse("""
                {"id":3,"groupId":2,"fromMemberId":6,"toMemberId":5,"amountOriginal":750.00,"currencyCode":"USD",
                 "fxRateToUsd":1,"amountUsd":750.00,"date":"2026-09-01","createdAt":"2026-09-01T10:00:00Z"}
                """));
        });

        var result = (await Import.ImportAsync(Site, later)).Body<ImportResult>();

        result.Created.Should().Be(1);
        (await _host.Ledger.ReportAsync(Site, Bob, 2)).Body<GroupReportDto>().MemberBalances.Should().OnlyContain(m => m.NetUsd == 0m);
    }

    [Fact]
    public async Task The_whole_payload_is_checked_before_anything_is_written_and_the_first_bad_row_is_named()
    {
        var payload = Mutated(n => n["settlements"]![1]!["toMemberId"] = 2); // its fromMemberId is 2 too

        var outcome = await Import.ImportAsync(Site, payload);

        outcome.ErrorCode(400).Should().Be("VALIDATION");
        Message(outcome).Should().Be("settlements[1] (id 2): toMemberId is the same member as fromMemberId.");
        _host.Rows.Rows.Should().BeEmpty();
    }

    public static TheoryData<string, string> BadRows() => new()
    {
        { "shares", "expenses[0] (id 1): shares: the amounts do not sum to amountUsd." },
        { "usd", "expenses[1] (id 2): amountUsd is not amountOriginal times fxRateToUsd, half-up to the cent." },
        { "negative", "expenses[2] (id 3): amountOriginal must be positive, with at most 2 decimals." },
        { "payer", "expenses[4] (id 5): payerMemberId is not a member of the group." },
        { "group", "expenses[6] (id 8): groupId is not a group of the payload or an imported group of the site." },
        { "split-number", "expenses[0] (id 1): splitMode is not Equal, Shares, Percentage or ExactAmounts." },
        { "storage-case", "expenses[0] (id 1): storageMode is not ConvertedToUsd or KeepOriginal." },
        { "target-case", "expenses[0] (id 1): targets[0]: type is not Member or Entity." },
        { "member-twice", "groups[1] (id 2): members[0] (id 1): id is used twice." },
        { "couple-of-three", "groups[0] (id 1): entities[0] (id 1): memberIds: a couple has exactly 2 members." },
        { "kind-case", "groups[0] (id 1): entities[0] (id 1): kind is not Couple or Subgroup." },
        { "creator", "groups[0] (id 1): creatorMemberId is not a member of the group." },
        { "currency", "settlements[0] (id 1): currencyCode is not a known currency." },
    };

    [Theory]
    [MemberData(nameof(BadRows))]
    public async Task A_bad_row_refuses_the_whole_payload_by_table_id_and_column_without_its_value(string fault, string message)
    {
        string? value = null;
        var payload = Mutated(n =>
        {
            switch (fault)
            {
                case "shares": n["expenses"]![0]!["shares"]![0]!["amountUsd"] = 33.33m; value = "33.33"; break;
                case "usd": n["expenses"]![1]!["amountUsd"] = 131.34m; value = "131.34"; break;
                case "negative": n["expenses"]![2]!["amountOriginal"] = -5000000m; value = "5000000"; break;
                case "payer": n["expenses"]![4]!["payerMemberId"] = 99; value = "99"; break;
                case "group": n["expenses"]![6]!["groupId"] = 77; value = "77"; break;
                case "split-number": n["expenses"]![0]!["splitMode"] = "0"; break;
                case "storage-case": n["expenses"]![0]!["storageMode"] = "keeporiginal"; value = "keeporiginal"; break;
                case "target-case": n["expenses"]![0]!["targets"]![0]!["type"] = "MEMBER"; value = "MEMBER"; break;
                case "member-twice": n["groups"]![1]!["members"]![0]!["id"] = 1; break;
                case "couple-of-three": n["groups"]![0]!["entities"]![0]!["memberIds"] = new JsonArray(1, 2, 3); break;
                case "kind-case": n["groups"]![0]!["entities"]![0]!["kind"] = "couple"; value = "couple"; break;
                case "creator": n["groups"]![0]!["creatorMemberId"] = 6; break;
                case "currency": n["settlements"]![0]!["currencyCode"] = "XXX"; value = "XXX"; break;
            }
        });

        var outcome = await Import.ImportAsync(Site, payload);

        outcome.ErrorCode(400).Should().Be("VALIDATION");
        Message(outcome).Should().Be(message);
        if (value is not null) Message(outcome).Should().NotContain(value);
        _host.Rows.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task An_empty_payload_is_refused() =>
        (await Import.ImportAsync(Site, new ImportRequest(null, null, null))).ErrorCode(400).Should().Be("VALIDATION");

    [Fact]
    public async Task It_never_writes_onto_what_the_site_made_itself()
    {
        var own = (await _host.Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("Made here"))).Body<GroupDto>(201);
        own.Id.Should().Be(1);
        var rows = _host.Rows.Rows.Count;

        var outcome = await Import.ImportAsync(Site, Fixture());

        outcome.ErrorCode(409).Should().Be("CONFLICT");
        Message(outcome).Should().Be("group 1 is a group the site made itself; nothing was written.");
        _host.Rows.Rows.Should().HaveCount(rows);
        (await _host.Ledger.GetGroupAsync(Site, Alice, 1)).Body<GroupDetailDto>().Name.Should().Be("Made here");
    }

    [Fact]
    public async Task New_records_after_an_import_take_the_next_ids()
    {
        await Import.ImportAsync(Site, Fixture());
        await _host.Ledger.GetGroupAsync(Site, Alice, 1); // Alice claims her member, and with it the group

        var group = (await _host.Ledger.CreateGroupAsync(Site, Alice, new GroupCreateRequest("Next"))).Body<GroupDto>(201);
        var member = (await _host.Ledger.AddMemberAsync(Site, Alice, 1, new MemberCreateRequest("Frida", null))).Body<MemberDto>(201);
        var expense = (await _host.Ledger.AddExpenseAsync(Site, Alice, 1, MoneyRoundTripTests.Expense(1, 5m, "USD", null, "Equal", 1, member.Id))).Body<ExpenseDto>(201);
        var settlement = (await _host.Ledger.AddSettlementAsync(Site, Alice, 1, new SettlementCreateRequest(2, 1, 1m, "USD", null, new DateOnly(2026, 10, 29)))).Body<SettlementDto>(201);

        // highest imported: group 2, member 6, expense 8, settlement 2 (and the new group's creator member is 7)
        (group.Id, member.Id, expense.Id, settlement.Id).Should().Be((3, 8, 9, 3));
    }

    [Fact]
    public async Task Accounts_claim_their_members_by_confirmed_email_and_the_creator_claims_the_group()
    {
        await Import.ImportAsync(Site, Fixture());
        var squatter = LedgerHost.Unconfirmed(13, "Alice");

        (await _host.Ledger.GetGroupAsync(Site, squatter, 1)).ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
        var detail = (await _host.Ledger.GetGroupAsync(Site, Alice, 1)).Body<GroupDetailDto>();

        detail.Members.Single(m => m.Id == 1).Should().Match<MemberDto>(m => m.UserId == 11 && m.IsMe);
        detail.CreatedByUserId.Should().Be(11);
        (await _host.Ledger.GetGroupAsync(Site, Bob, 1)).Body<GroupDetailDto>().CreatedByUserId.Should().Be(11, "Bob claims his member, not the group");
        (await _host.Ledger.DeleteGroupAsync(Site, Bob, 1)).ErrorCode(403).Should().Be("FORBIDDEN");
    }

    [Fact]
    public async Task An_imported_groups_creator_id_0_matches_no_caller()
    {
        await Import.ImportAsync(Site, Fixture());
        (await _host.Store.LoadAsync<Group>(Site, DelatoKind.Group, 1))!.CreatedByUserId.Should().Be(0);

        foreach (var nobody in new[] { new DelatoCaller(0, "Zero", null), new DelatoCaller(0, "Zero", "alice@example.test"), new DelatoCaller(-1, "Negative", null) })
        {
            DelatoLedger.CanSee((await _host.Store.LoadAsync<Group>(Site, DelatoKind.Group, 1))!, nobody).Should().BeFalse();
            (await _host.Ledger.GetGroupAsync(Site, nobody, 1)).ErrorCode(404).Should().Be("GROUP_NOT_FOUND");
            (await _host.Ledger.ListGroupsAsync(Site, nobody)).Body<List<GroupSummaryDto>>().Should().BeEmpty();
        }
        (await _host.Store.LoadAsync<Group>(Site, DelatoKind.Group, 1))!.Members.Should().OnlyContain(m => m.UserId == null);
    }

    [Fact]
    public async Task The_creator_claim_and_the_member_claim_are_one_write()
    {
        var rows = new CountingRepository(new InMemoryRepository<DelatoDocument>(_host.Tenants));
        var store = new DelatoStore(rows, _host.Tenants, _host.Clock);
        await new DelatoImport(store).ImportAsync(Site, Fixture());
        rows.Updates.Clear();

        await new DelatoLedger(store, _host.Clock).GetGroupAsync(Site, Alice, 1);

        rows.Updates.Should().ContainSingle("the member's account and the group's creator are set in the same write")
            .Which.Should().Match<DelatoDocument>(d => d.Kind == DelatoKind.Group && d.Number == 1);
        var group = (await store.LoadAsync<Group>(Site, DelatoKind.Group, 1))!;
        (group.CreatedByUserId, group.Members.Single(m => m.Id == 1).UserId).Should().Be((11, 11));
    }

    [Fact]
    public async Task Imported_records_keep_their_own_creation_times_in_UTC()
    {
        await Import.ImportAsync(Site, Fixture());

        var group = (await _host.Store.LoadAsync<Group>(Site, DelatoKind.Group, 1))!;
        group.CreatedAt.Kind.Should().Be(DateTimeKind.Utc);
        group.CreatedAt.Should().Be(Fixture().Groups![0].CreatedAt.ToUniversalTime());
        group.Imported.Should().BeTrue();
        (await _host.Store.LoadAsync<Expense>(Site, DelatoKind.Expense, 1, 1))!.Date.Should().Be(new DateOnly(2026, 8, 20));
    }

    [Fact]
    public void Only_the_sites_Host_or_Administrators_may_import()
    {
        var authorize = typeof(ImportController).GetCustomAttribute<AuthorizeAttribute>()!;

        authorize.Roles!.Split(',').Should().BeEquivalentTo([RoleNames.Host, RoleNames.Admin]);
        typeof(ImportController).Should().BeDerivedFrom<DelatoController>();
    }

    [Theory]
    [InlineData("Couple", true)]
    [InlineData("couple", false)]
    [InlineData("COUPLE", false)]
    [InlineData("0", false)]
    [InlineData(" Couple", false)]
    [InlineData(null, false)]
    public void Names_are_read_exactly(string? text, bool read) =>
        DelatoImport.Exact<EntityKind>(text, out _).Should().Be(read);

    /// <summary>A repository that counts its updates, over the in-memory one.</summary>
    private sealed class CountingRepository(InMemoryRepository<DelatoDocument> inner) : IRepository<DelatoDocument, Guid>
    {
        public List<DelatoDocument> Updates { get; } = [];
        public Task<DelatoDocument?> GetByIdAsync(Guid id, CancellationToken ct = default) => inner.GetByIdAsync(id, ct);
        public Task<IReadOnlyList<DelatoDocument>> GetAllAsync(CancellationToken ct = default) => inner.GetAllAsync(ct);
        public Task AddAsync(DelatoDocument aggregate, CancellationToken ct = default) => inner.AddAsync(aggregate, ct);
        public Task UpdateAsync(DelatoDocument aggregate, CancellationToken ct = default) { Updates.Add(aggregate); return inner.UpdateAsync(aggregate, ct); }
        public Task DeleteAsync(DelatoDocument aggregate, CancellationToken ct = default) => inner.DeleteAsync(aggregate, ct);
        public Task<IReadOnlyList<DelatoDocument>> FindAsync(Expression<Func<DelatoDocument, bool>> predicate, CancellationToken ct = default) => inner.FindAsync(predicate, ct);
    }
}
