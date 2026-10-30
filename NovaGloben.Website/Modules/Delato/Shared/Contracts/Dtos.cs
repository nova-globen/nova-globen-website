namespace Delato.Shared.Contracts;

// The module's HTTP contract under {site}/api/v1, shared by its server and its web client. Money is decimal, written
// as JSON numbers. Balances, transfers and settle-up are always in USD, the one settlement currency; a group's
// BaseCurrency is shown, never used to compute. An expense keeps its original amount, currency and rate to USD.

// ---- groups ----
public record GroupCreateRequest(string Name);
public record GroupDto(int Id, string Name, string BaseCurrency, int CreatedByUserId);
public record GroupSummaryDto(int Id, string Name, string BaseCurrency, int CreatedByUserId, int MemberCount, decimal MyNetBalanceUsd);
public record GroupDetailDto(int Id, string Name, string BaseCurrency, int CreatedByUserId, List<MemberDto> Members, List<EntityDto> Entities);

// ---- members / couples and subgroups ----
public record MemberCreateRequest(string Name, string? Email);

/// <summary>
/// A member as the group's own members see it. <see cref="UserId"/> is set only once an account with a confirmed
/// e-mail has claimed the member; <see cref="IsMe"/> says whether that is the caller. The e-mail is shown only here,
/// to the group's members.
/// </summary>
public record MemberDto(int Id, int GroupId, string Name, int? UserId, string? Email, bool IsMe);
public record EntityCreateRequest(string Name, string Kind, List<int> MemberIds);
public record EntityDto(int Id, int GroupId, string Name, string Kind, List<int> MemberIds);

// ---- expenses ----
public record ExpenseTargetDto(string Type, int Id, decimal? Value);
public record ExpenseCreateRequest(
    string Description, DateOnly Date, int PayerMemberId,
    decimal Amount, string CurrencyCode, decimal? FxRateToUsd,
    string? StorageMode, string SplitMode, List<ExpenseTargetDto> Targets);
public record ExpenseShareDto(int MemberId, decimal AmountUsd);
public record ExpenseDto(
    int Id, int GroupId, string Description, DateOnly Date, int PayerMemberId,
    decimal AmountOriginal, string CurrencyCode, decimal FxRateToUsd, decimal AmountUsd,
    string StorageMode, string SplitMode, List<ExpenseTargetDto> Targets, List<ExpenseShareDto> Shares);

// ---- settlements (settle-up is in USD; a payment in another currency carries its rate) ----
public record SettlementCreateRequest(int FromMemberId, int ToMemberId, decimal Amount, string CurrencyCode, decimal? FxRateToUsd, DateOnly Date);
public record SettlementDto(int Id, int GroupId, int FromMemberId, int ToMemberId, decimal AmountUsd, decimal AmountOriginal, string CurrencyCode, decimal FxRateToUsd, DateOnly Date);

// ---- reports (all USD) ----
public record MemberBalanceDto(int MemberId, string Name, decimal NetUsd);
public record EntityBalanceDto(int EntityId, string Name, string Kind, decimal NetUsd);
public record MemberTransferDto(int FromMemberId, int ToMemberId, decimal AmountUsd);
public record EntityTransferDto(int FromEntityId, int ToEntityId, decimal AmountUsd);
public record GroupReportDto(
    int GroupId, string BaseCurrency, decimal TotalSpentUsd,
    List<MemberBalanceDto> MemberBalances,
    List<EntityBalanceDto> EntityBalances,
    List<MemberTransferDto> PairwiseDebts,
    List<MemberTransferDto> SuggestedTransfers,
    List<EntityTransferDto> SuggestedEntityTransfers);

// "My summary" names members, never shows their e-mail.
public record OwedToDto(int ToMemberId, string ToName, decimal AmountUsd);
public record OwedFromDto(int FromMemberId, string FromName, decimal AmountUsd);
public record MyGroupSummaryDto(int GroupId, string GroupName, decimal MyNetUsd, List<OwedToDto> IShouldPay, List<OwedFromDto> IShouldReceive);
public record MySummaryDto(decimal TotalNetUsd, List<MyGroupSummaryDto> Groups);

// ---- a person's request (site administrators) ----
/// <summary>Forget the person with this e-mail address in every group where they were added without an account.</summary>
public record ForgetRequest(string? Email);
/// <summary>How many members were forgotten, in how many groups.</summary>
public record ForgetResult(int Members, int Groups);

// ---- misc ----
public record CurrencyDto(string Code, string Symbol, string NameEn, string NameFa);

/// <summary>Every error answers <c>{ "error": { "code", "message" } }</c>.</summary>
public record ErrorEnvelope(ErrorBody Error);
public record ErrorBody(string Code, string Message);
