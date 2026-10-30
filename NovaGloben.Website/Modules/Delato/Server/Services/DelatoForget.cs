using Delato.Server.Storage;
using Delato.Shared.Contracts;
using Delato.Shared.Domain;

namespace Delato.Server.Services;

/// <summary>
/// A request from someone a member added to a group by name and e-mail, who has no account: a site administrator
/// enters their address, and every member with that address that no account has claimed is renamed
/// <see cref="DelatoUserErasure.ErasedName"/> and loses its e-mail, in every group of the site. The groups, expenses
/// and payments stay and still balance, as for a deleted account. A member an account has claimed is left alone: that
/// person deletes their account themselves. Idempotent: a second run finds nothing to do.
/// </summary>
public sealed class DelatoForget(DelatoStore store)
{
    public async Task<ForgetResult> ForgetEmailAsync(int siteId, string? email, CancellationToken ct = default)
    {
        var address = DelatoCaller.NormalizeEmail(email);
        if (siteId <= 0 || address is null) return new ForgetResult(0, 0);

        var members = 0;
        var groups = 0;
        foreach (var group in await store.AllAsync<Group>(siteId, DelatoKind.Group, ct))
        {
            var theirs = group.Members.Where(m => m.UserId is null && DelatoCaller.NormalizeEmail(m.Email) == address).ToList();
            if (theirs.Count == 0) continue;
            foreach (var member in theirs)
            {
                member.Name = DelatoUserErasure.ErasedName;
                member.Email = null;
            }
            await store.StoreAsync(siteId, DelatoKind.Group, group.Id, 0, group, ct);
            members += theirs.Count;
            groups++;
        }
        return new ForgetResult(members, groups);
    }
}