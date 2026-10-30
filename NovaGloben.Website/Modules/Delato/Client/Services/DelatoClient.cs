using Delato.Shared.Contracts;
using Stomme.Services;
using Stomme.Shared;

namespace Delato.Client.Services;

/// <summary>
/// The module's API, from the page: <c>{site}/api/v1/...</c>. Stomme's ServiceBase sends the signed-in user's
/// credentials and the antiforgery header; a refusal throws <see cref="ServiceRefusalException"/> carrying the
/// server's reason, which the page shows.
/// </summary>
public sealed class DelatoClient(HttpClient http, SiteState siteState) : ServiceBase(http, siteState)
{
    protected override bool ThrowOnRefusal => true;

    private string Api(string path) => CreateApiUrl("v1/" + path);

    public async Task<List<CurrencyDto>> CurrenciesAsync() => await GetJsonAsync<List<CurrencyDto>>(Api("currencies")) ?? [];

    // groups
    public async Task<List<GroupSummaryDto>> GroupsAsync() => await GetJsonAsync<List<GroupSummaryDto>>(Api("groups")) ?? [];
    public Task<GroupDto> CreateGroupAsync(string name) => PostJsonAsync<GroupCreateRequest, GroupDto>(Api("groups"), new(name));
    public Task<GroupDetailDto> GroupAsync(int id) => GetJsonAsync<GroupDetailDto>(Api($"groups/{id}"));
    public Task<GroupDto> RenameGroupAsync(int id, string name) => PutJsonAsync<GroupCreateRequest, GroupDto>(Api($"groups/{id}"), new(name));
    public Task DeleteGroupAsync(int id) => DeleteAsync(Api($"groups/{id}"));

    // members, couples and subgroups
    public Task<MemberDto> AddMemberAsync(int groupId, string name, string? email) =>
        PostJsonAsync<MemberCreateRequest, MemberDto>(Api($"groups/{groupId}/members"), new(name, string.IsNullOrWhiteSpace(email) ? null : email));
    public Task DeleteMemberAsync(int groupId, int memberId) => DeleteAsync(Api($"groups/{groupId}/members/{memberId}"));
    public Task<EntityDto> AddEntityAsync(int groupId, string name, string kind, List<int> memberIds) =>
        PostJsonAsync<EntityCreateRequest, EntityDto>(Api($"groups/{groupId}/entities"), new(name, kind, memberIds));
    public Task DeleteEntityAsync(int groupId, int entityId) => DeleteAsync(Api($"groups/{groupId}/entities/{entityId}"));

    // expenses
    public async Task<List<ExpenseDto>> ExpensesAsync(int groupId) => await GetJsonAsync<List<ExpenseDto>>(Api($"groups/{groupId}/expenses")) ?? [];
    public Task<ExpenseDto> AddExpenseAsync(int groupId, ExpenseCreateRequest request) =>
        PostJsonAsync<ExpenseCreateRequest, ExpenseDto>(Api($"groups/{groupId}/expenses"), request);
    public Task<ExpenseDto> UpdateExpenseAsync(int groupId, int expenseId, ExpenseCreateRequest request) =>
        PutJsonAsync<ExpenseCreateRequest, ExpenseDto>(Api($"groups/{groupId}/expenses/{expenseId}"), request);
    public Task DeleteExpenseAsync(int groupId, int expenseId) => DeleteAsync(Api($"groups/{groupId}/expenses/{expenseId}"));

    // settlements, the report and my summary (all USD)
    public async Task<List<SettlementDto>> SettlementsAsync(int groupId) => await GetJsonAsync<List<SettlementDto>>(Api($"groups/{groupId}/settlements")) ?? [];
    public Task<SettlementDto> AddSettlementAsync(int groupId, SettlementCreateRequest request) =>
        PostJsonAsync<SettlementCreateRequest, SettlementDto>(Api($"groups/{groupId}/settlements"), request);
    public Task DeleteSettlementAsync(int groupId, int settlementId) => DeleteAsync(Api($"groups/{groupId}/settlements/{settlementId}"));
    public Task<GroupReportDto> ReportAsync(int groupId) => GetJsonAsync<GroupReportDto>(Api($"groups/{groupId}/report"));
    public Task<MySummaryDto> MySummaryAsync() => GetJsonAsync<MySummaryDto>(Api("me/summary"));

    // the one-time import (the server allows it to the site's Host or Administrators only)
    public Task<ImportResult> ImportAsync(ImportRequest request) => PostJsonAsync<ImportRequest, ImportResult>(Api("import"), request);

    // a person added to groups without an account asks to be forgotten (site administrators)
    public Task<ForgetResult> ForgetAsync(string email) => PostJsonAsync<ForgetRequest, ForgetResult>(Api("forget"), new(email));
}
