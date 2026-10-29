using System.Reflection;
using System.Security.Claims;
using Delato.Server.Services;
using Stomme.Managers;
using Stomme.Models;

namespace Delato.Tests;

/// <summary>The caller from the site's Stomme user: the e-mail reaches Delato only when the account confirmed it.</summary>
public sealed class StommeCallerTests
{
    /// <summary>An IUserManager that knows one user and answers nothing else.</summary>
    public class OneUser : DispatchProxy
    {
        public User? Known { get; set; }

        protected override object? Invoke(MethodInfo? method, object?[]? args) =>
            method?.Name == nameof(IUserManager.GetUserAsync) && args is [int id, int] && Known?.UserId == id
                ? Task.FromResult(Known)
                : method?.ReturnType == typeof(Task<User>) ? Task.FromResult<User>(null!) : throw new NotSupportedException(method?.Name);
    }

    private static Task<DelatoCaller?> CallerFor(User? user, string? claim)
    {
        var users = DispatchProxy.Create<IUserManager, OneUser>();
        ((OneUser)(object)users).Known = user;
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            claim is null ? [] : [new Claim(ClaimTypes.NameIdentifier, claim)], "test"));
        return new StommeDelatoCallers(users).CurrentAsync(principal, 1);
    }

    [Fact]
    public async Task A_confirmed_email_is_passed_on_normalised()
    {
        var caller = await CallerFor(new User { UserId = 4, Username = "bob", DisplayName = "Bob", Email = " Bob@Example.TEST ", EmailConfirmed = true }, "4");

        caller.Should().Be(new DelatoCaller(4, "Bob", "bob@example.test"));
    }

    [Fact]
    public async Task An_unconfirmed_email_is_never_passed_on()
    {
        var caller = await CallerFor(new User { UserId = 4, Username = "bob", DisplayName = "", Email = "bob@example.test", EmailConfirmed = false }, "4");

        caller.Should().Be(new DelatoCaller(4, "bob", null));
    }

    [Fact]
    public async Task No_sign_in_or_no_account_is_no_caller()
    {
        (await CallerFor(null, null)).Should().BeNull();
        (await CallerFor(null, "4")).Should().BeNull();
        (await CallerFor(new User { UserId = 4, Username = "gone", Email = "x@example.test", EmailConfirmed = true, IsDeleted = true }, "4")).Should().BeNull();
    }
}
