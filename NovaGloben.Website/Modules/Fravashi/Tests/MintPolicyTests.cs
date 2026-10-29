using Fravashi.Broker.Identity;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

/// <summary>
/// Minting is deny-by-default: authenticating at the IdP is not entitlement to
/// spend the organization's LLM budget.
/// </summary>
public class MintPolicyTests
{
    private static readonly ValidatedIdentity Ada =
        new("oidc:2b1c0f3e-8a4d-4c6e-9f10-000000000001", "ada@example.com", "ada");

    [Fact]
    public void An_empty_allowlist_allows_nobody()
    {
        MintPolicy.IsAllowed(Ada, []).Should().BeFalse();
    }

    [Theory]
    [InlineData("oidc:2b1c0f3e-8a4d-4c6e-9f10-000000000001")] // the canonical id
    [InlineData("2b1c0f3e-8a4d-4c6e-9f10-000000000001")]      // the bare subject
    [InlineData("ada@example.com")]                            // the email an operator would actually write
    [InlineData("ADA@EXAMPLE.COM")]                            // case-insensitive
    [InlineData("ada")]                                        // the username
    [InlineData("  ada@example.com  ")]                        // tolerant of stray whitespace
    public void Any_of_the_identity_s_names_may_be_listed(string entry)
    {
        MintPolicy.IsAllowed(Ada, [entry]).Should().BeTrue();
    }

    [Fact]
    public void A_stranger_who_authenticated_is_still_refused()
    {
        var stranger = new ValidatedIdentity("oidc:99999999-0000-0000-0000-000000000000", "eve@elsewhere.test", "eve");

        MintPolicy.IsAllowed(stranger, ["ada@example.com", "bob@example.com"]).Should().BeFalse();
    }

    [Fact]
    public void A_domain_entry_is_NOT_a_wildcard()
    {
        // Explicit entries only: a bare domain must not silently admit everyone on it.
        MintPolicy.IsAllowed(Ada, ["@example.com"]).Should().BeFalse();
        MintPolicy.IsAllowed(Ada, ["example.com"]).Should().BeFalse();
    }

    [Fact]
    public void Blank_entries_are_ignored_rather_than_matching_everything()
    {
        var noClaims = new ValidatedIdentity("oidc:abc");

        MintPolicy.IsAllowed(noClaims, ["", "   "]).Should().BeFalse();
        MintPolicy.IsAllowed(noClaims, ["", "oidc:abc"]).Should().BeTrue();
    }

    // ---- who may read what the broker has been doing, and lift a suspension ----------------

    [Fact]
    public void With_nobody_named_an_operator_nobody_is_one()
    {
        // Absence of configuration is never a permission here, exactly as it is not for minting.
        var options = new BrokerIdentityOptions();

        MintPolicy.IsOperator(new ValidatedIdentity("oidc:abc", "ada@example.com", "ada"), options).Should().BeFalse();
    }

    [Fact]
    public void An_operator_is_named_the_same_way_a_user_is()
    {
        var options = new BrokerIdentityOptions { Operators = ["ada@example.com"] };

        MintPolicy.IsOperator(new ValidatedIdentity("oidc:abc", "ada@example.com", "ada"), options).Should().BeTrue();
        MintPolicy.IsOperator(new ValidatedIdentity("oidc:def", "bob@example.com", "bob"), options).Should().BeFalse();
    }

    [Fact]
    public void A_role_the_provider_vouches_for_makes_an_operator_too()
    {
        var options = new BrokerIdentityOptions { OperatorRole = "studio-operator" };

        MintPolicy.IsOperator(new ValidatedIdentity("oidc:abc", "ada@example.com", "ada") { Roles = ["studio-operator"] }, options).Should().BeTrue();
        MintPolicy.IsOperator(new ValidatedIdentity("oidc:def", "bob@example.com", "bob") { Roles = ["engineer"] }, options).Should().BeFalse();
    }

    [Fact]
    public void Being_allowed_to_mint_does_not_make_somebody_an_operator()
    {
        // The two lists answer different questions: spending the grant is not reading what everyone spent.
        var options = new BrokerIdentityOptions { Allowlist = ["ada@example.com"] };

        MintPolicy.IsOperator(new ValidatedIdentity("oidc:abc", "ada@example.com", "ada"), options).Should().BeFalse();
    }
}
