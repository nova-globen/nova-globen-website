namespace Delato.Shared.Domain;

public enum EntityKind { Couple, Subgroup }
public enum StorageMode { ConvertedToUsd, KeepOriginal }
public enum SplitMode { Equal, Shares, Percentage, ExactAmounts }
public enum TargetType { Member, Entity }

public class Group
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string BaseCurrency { get; set; } = "USD";
    public int CreatedByUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<Member> Members { get; set; } = [];
    public List<GroupEntity> Entities { get; set; } = [];

    /// <summary>True for a group brought in by the import; only such a group may be overwritten by a later import.</summary>
    public bool Imported { get; set; }

    /// <summary>
    /// For an imported group: the member its creator was. The import cannot know the creator's account here, so
    /// <see cref="CreatedByUserId"/> starts at 0 and becomes the account that claims this member (by confirmed e-mail).
    /// </summary>
    public int? CreatorMemberId { get; set; }
}

public class Member
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public required string Name { get; set; }
    /// <summary>The folder site's Stomme account this member is linked to (by e-mail), if any.</summary>
    public int? UserId { get; set; }
    public string? Email { get; set; }
}

public class GroupEntity
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public required string Name { get; set; }
    public EntityKind Kind { get; set; }
    public List<EntityMember> EntityMembers { get; set; } = [];
}

public class EntityMember
{
    public int GroupEntityId { get; set; }
    public int MemberId { get; set; }
}

public class Expense
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public required string Description { get; set; }
    public DateOnly Date { get; set; }
    public int PayerMemberId { get; set; }
    public decimal AmountOriginal { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal FxRateToUsd { get; set; }
    public decimal AmountUsd { get; set; }
    public StorageMode StorageMode { get; set; }
    public SplitMode SplitMode { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<ExpenseTarget> Targets { get; set; } = [];
    public List<ExpenseShare> Shares { get; set; } = [];
}

public class ExpenseTarget
{
    public int Id { get; set; }
    public int ExpenseId { get; set; }
    public TargetType Type { get; set; }
    public int TargetId { get; set; }
    public decimal? Value { get; set; }
}

public class ExpenseShare
{
    public int Id { get; set; }
    public int ExpenseId { get; set; }
    public int MemberId { get; set; }
    public decimal AmountUsd { get; set; }
}

public class Settlement
{
    public int Id { get; set; }
    public int GroupId { get; set; }
    public int FromMemberId { get; set; }
    public int ToMemberId { get; set; }
    public decimal AmountOriginal { get; set; }
    public required string CurrencyCode { get; set; }
    public decimal FxRateToUsd { get; set; }
    public decimal AmountUsd { get; set; }
    public DateOnly Date { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
