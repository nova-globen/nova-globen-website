namespace Delato.Shared.Contracts;

// POST {site}/api/v1/import - bringing a whole earlier ledger in, once, for the site's Host or Administrators.
// Every id is the earlier ledger's own. Records are created if absent and skipped if present. Money is decimal, as
// JSON numbers. Enum-like fields carry their exact names (Couple, Subgroup, Member, Entity, Equal, ...).

public record ImportRequest(List<ImportGroup>? Groups, List<ImportExpense>? Expenses, List<ImportSettlement>? Settlements);

/// <summary>A group with its members and couples/subgroups. <see cref="CreatorMemberId"/> is the member its creator was.</summary>
public record ImportGroup(int Id, string Name, string BaseCurrency, DateTime CreatedAt, int? CreatorMemberId,
    List<ImportMember>? Members, List<ImportEntity>? Entities);

/// <summary>A member: a name and, optionally, the e-mail an account claims it by (once confirmed).</summary>
public record ImportMember(int Id, string Name, string? Email);

public record ImportEntity(int Id, string Name, string Kind, List<int>? MemberIds);

public record ImportExpense(int Id, int GroupId, string Description, DateOnly Date, int PayerMemberId,
    decimal AmountOriginal, string CurrencyCode, decimal FxRateToUsd, decimal AmountUsd,
    string StorageMode, string SplitMode, DateTime CreatedAt, List<ImportTarget>? Targets, List<ImportShare>? Shares);

public record ImportTarget(string Type, int TargetId, decimal? Value);

public record ImportShare(int MemberId, decimal AmountUsd);

public record ImportSettlement(int Id, int GroupId, int FromMemberId, int ToMemberId,
    decimal AmountOriginal, string CurrencyCode, decimal FxRateToUsd, decimal AmountUsd, DateOnly Date, DateTime CreatedAt);

/// <summary>
/// What an import did: the records the payload carried, how many it created, and the ones it skipped because they were
/// already on the site (as "group 3", "expense 17", "settlement 2"). A second run of the same payload creates nothing.
/// </summary>
public record ImportResult(int Groups, int Members, int Entities, int Expenses, int Settlements, int Created, List<string> Skipped);
