using Delato.Server.Storage;
using Delato.Shared.Domain;
using Stomme.Erasure;

namespace Delato.Server.Services;

/// <summary>
/// A person deleted from the Delato site. A group's ledger is shared by all its members - who paid what, and who owes
/// whom - so the group, its expenses and settlements stay and still balance, with the person's member renamed
/// <see cref="ErasedName"/>. What ties that member to the person goes: their name, the link to their account and their
/// e-mail address (which would let a new account with the same address claim the member again), and the account
/// recorded as creator of any group they started. Idempotent: a second run finds nothing to do.
/// </summary>
public sealed class DelatoUserErasure(DelatoStore store) : ISubjectErasureParticipant
{
    public const string GroupsStore = "Delato groups";

    /// <summary>What the group calls the member once the person is gone: stored data, so one name for every language.</summary>
    public const string ErasedName = "Former member";

    public string Name => "Delato";

    public IReadOnlyList<ErasureDeclaration> Declarations { get; } = new[] { ErasureDeclaration.Anonymise(GroupsStore) };

    public async Task<IReadOnlyList<ErasureOutcome>> EraseAsync(ErasureSubject subject, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        var changed = 0;
        if (subject.SiteId > 0 && subject.UserId > 0)
        {
            foreach (var group in await store.AllAsync<Group>(subject.SiteId, DelatoKind.Group, ct))
            {
                var touched = false;
                foreach (var member in group.Members.Where(m => m.UserId == subject.UserId))
                {
                    member.Name = ErasedName;
                    member.UserId = null;
                    member.Email = null;
                    touched = true;
                }
                if (group.CreatedByUserId == subject.UserId)
                {
                    group.CreatedByUserId = 0;
                    touched = true;
                }
                if (!touched) continue;
                await store.StoreAsync(subject.SiteId, DelatoKind.Group, group.Id, 0, group, ct);
                changed++;
            }
        }
        return new[] { new ErasureOutcome(GroupsStore, ErasureHandling.Anonymise, changed) };
    }
}