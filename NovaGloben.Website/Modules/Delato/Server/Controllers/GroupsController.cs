using Delato.Server.Services;
using Delato.Shared.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Delato.Server.Controllers;

/// <summary>
/// {site}/api/v1/groups and everything inside a group: members, couples and subgroups, expenses, settlements and the
/// group's report. Every call is for the signed-in caller; the ledger decides what they may see.
/// </summary>
[Route("api/v1/groups")]
[Authorize]
public sealed class GroupsController(DelatoLedger ledger) : DelatoController
{
    private CancellationToken Ct => HttpContext.RequestAborted;

    // ---- groups
    [HttpPost]
    public Task<IActionResult> CreateGroup(GroupCreateRequest req) => AsCaller(c => ledger.CreateGroupAsync(Site.SiteId, c, req, Ct));

    [HttpGet]
    public Task<IActionResult> ListGroups() => AsCaller(c => ledger.ListGroupsAsync(Site.SiteId, c, Ct));

    [HttpGet("{groupId:int}")]
    public Task<IActionResult> GetGroup(int groupId) => AsCaller(c => ledger.GetGroupAsync(Site.SiteId, c, groupId, Ct));

    [HttpPut("{groupId:int}")]
    public Task<IActionResult> RenameGroup(int groupId, GroupCreateRequest req) => AsCaller(c => ledger.RenameGroupAsync(Site.SiteId, c, groupId, req, Ct));

    [HttpDelete("{groupId:int}")]
    public Task<IActionResult> DeleteGroup(int groupId) => AsCaller(c => ledger.DeleteGroupAsync(Site.SiteId, c, groupId, Ct));

    // ---- members
    [HttpPost("{groupId:int}/members")]
    public Task<IActionResult> AddMember(int groupId, MemberCreateRequest req) => AsCaller(c => ledger.AddMemberAsync(Site.SiteId, c, groupId, req, Ct));

    [HttpGet("{groupId:int}/members")]
    public Task<IActionResult> ListMembers(int groupId) => AsCaller(c => ledger.ListMembersAsync(Site.SiteId, c, groupId, Ct));

    [HttpDelete("{groupId:int}/members/{memberId:int}")]
    public Task<IActionResult> DeleteMember(int groupId, int memberId) => AsCaller(c => ledger.DeleteMemberAsync(Site.SiteId, c, groupId, memberId, Ct));

    // ---- couples and subgroups
    [HttpPost("{groupId:int}/entities")]
    public Task<IActionResult> AddEntity(int groupId, EntityCreateRequest req) => AsCaller(c => ledger.AddEntityAsync(Site.SiteId, c, groupId, req, Ct));

    [HttpGet("{groupId:int}/entities")]
    public Task<IActionResult> ListEntities(int groupId) => AsCaller(c => ledger.ListEntitiesAsync(Site.SiteId, c, groupId, Ct));

    [HttpDelete("{groupId:int}/entities/{entityId:int}")]
    public Task<IActionResult> DeleteEntity(int groupId, int entityId) => AsCaller(c => ledger.DeleteEntityAsync(Site.SiteId, c, groupId, entityId, Ct));

    // ---- expenses
    [HttpPost("{groupId:int}/expenses")]
    public Task<IActionResult> AddExpense(int groupId, ExpenseCreateRequest req) => AsCaller(c => ledger.AddExpenseAsync(Site.SiteId, c, groupId, req, Ct));

    [HttpGet("{groupId:int}/expenses")]
    public Task<IActionResult> ListExpenses(int groupId) => AsCaller(c => ledger.ListExpensesAsync(Site.SiteId, c, groupId, Ct));

    [HttpGet("{groupId:int}/expenses/{expenseId:int}")]
    public Task<IActionResult> GetExpense(int groupId, int expenseId) => AsCaller(c => ledger.GetExpenseAsync(Site.SiteId, c, groupId, expenseId, Ct));

    [HttpPut("{groupId:int}/expenses/{expenseId:int}")]
    public Task<IActionResult> UpdateExpense(int groupId, int expenseId, ExpenseCreateRequest req) => AsCaller(c => ledger.UpdateExpenseAsync(Site.SiteId, c, groupId, expenseId, req, Ct));

    [HttpDelete("{groupId:int}/expenses/{expenseId:int}")]
    public Task<IActionResult> DeleteExpense(int groupId, int expenseId) => AsCaller(c => ledger.DeleteExpenseAsync(Site.SiteId, c, groupId, expenseId, Ct));

    // ---- settlements (USD)
    [HttpPost("{groupId:int}/settlements")]
    public Task<IActionResult> AddSettlement(int groupId, SettlementCreateRequest req) => AsCaller(c => ledger.AddSettlementAsync(Site.SiteId, c, groupId, req, Ct));

    [HttpGet("{groupId:int}/settlements")]
    public Task<IActionResult> ListSettlements(int groupId) => AsCaller(c => ledger.ListSettlementsAsync(Site.SiteId, c, groupId, Ct));

    [HttpDelete("{groupId:int}/settlements/{settlementId:int}")]
    public Task<IActionResult> DeleteSettlement(int groupId, int settlementId) => AsCaller(c => ledger.DeleteSettlementAsync(Site.SiteId, c, groupId, settlementId, Ct));

    // ---- report (USD)
    [HttpGet("{groupId:int}/report")]
    public Task<IActionResult> Report(int groupId) => AsCaller(c => ledger.ReportAsync(Site.SiteId, c, groupId, Ct));
}

/// <summary>{site}/api/v1/me: the caller's own summary across their groups, in USD.</summary>
[Route("api/v1/me")]
[Authorize]
public sealed class MeController(DelatoLedger ledger) : DelatoController
{
    [HttpGet("summary")]
    public Task<IActionResult> Summary() => AsCaller(c => ledger.MySummaryAsync(Site.SiteId, c, HttpContext.RequestAborted));
}

/// <summary>{site}/api/v1/currencies: the currencies an expense or payment may be entered in.</summary>
[Route("api/v1/currencies")]
[AllowAnonymous]
public sealed class CurrenciesController : DelatoController
{
    [HttpGet]
    public IActionResult List() => Ok(Currencies.All);
}
