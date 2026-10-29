using System.Security.Claims;
using Stomme.Managers;

namespace Delato.Server.Services;

/// <summary>
/// The signed-in account a request is for, as Delato needs it: its id, a name to show, and its e-mail ONLY when the
/// account has confirmed it. An unconfirmed e-mail is never used to link the caller to a member: otherwise anyone
/// registering with another person's address would inherit their groups and balances.
/// </summary>
public sealed record DelatoCaller(int UserId, string Name, string? ConfirmedEmail)
{
    /// <summary>The form every member e-mail is stored and compared in.</summary>
    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
}

/// <summary>Who the request is for; null when it is anonymous or the account is gone.</summary>
public interface IDelatoCallers
{
    Task<DelatoCaller?> CurrentAsync(ClaimsPrincipal principal, int siteId);
}

/// <summary>The caller from the request's Stomme sign-in (cookie or JWT) and the site's user record.</summary>
public sealed class StommeDelatoCallers(IUserManager users) : IDelatoCallers
{
    public async Task<DelatoCaller?> CurrentAsync(ClaimsPrincipal principal, int siteId)
    {
        if (!int.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var userId) || userId <= 0)
        {
            return null;
        }
        var user = await users.GetUserAsync(userId, siteId);
        if (user is null || user.IsDeleted)
        {
            return null;
        }
        var name = string.IsNullOrWhiteSpace(user.DisplayName) ? user.Username : user.DisplayName;
        var email = user.EmailConfirmed ? DelatoCaller.NormalizeEmail(user.Email) : null;
        return new DelatoCaller(userId, name, email);
    }
}
