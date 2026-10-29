using System.Text;
using Fravashi.Broker.Configuration;
using Fravashi.Broker.Revocation;
using Fravashi.Broker.Tokens;
using AwesomeAssertions;
using Xunit;

namespace Fravashi.Broker.Tests;

public sealed class TokenServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);
    private const string Scope = "messages";

    private static BrokerSecrets MakeSecrets(string signing = "this-is-a-32-byte-signing-secret!!") =>
        BrokerSecrets.FromEnvironment(name => name switch
        {
            BrokerSecrets.OrgKeyEnvVar => "sk-ant-TESTKEY-do-not-use",
            BrokerSecrets.SigningSecretEnvVar => signing,
            _ => null,
        });

    private static (TokenService Service, TestTimeProvider Clock, InMemoryRevocationStore Revocations) Build(
        BrokerSecrets? secrets = null)
    {
        var clock = new TestTimeProvider(Now);
        var revocations = new InMemoryRevocationStore();
        var service = new TokenService(secrets ?? MakeSecrets(), revocations, clock);
        return (service, clock, revocations);
    }

    [Fact]
    public void Valid_token_round_trips()
    {
        var (service, _, _) = Build();

        var (token, claims) = service.Mint("alice", Scope, TimeSpan.FromHours(1));
        var result = service.Validate(token, Scope);

        result.Status.Should().Be(TokenValidationStatus.Valid);
        result.Token!.Subject.Should().Be("alice");
        result.Token.TokenId.Should().Be(claims.TokenId);
        result.Token.Scope.Should().Be(Scope);
    }

    [Fact]
    public void Tampered_signature_is_rejected()
    {
        var (service, _, _) = Build();
        var (token, _) = service.Mint("alice", Scope, TimeSpan.FromHours(1));

        // Flip a character in the signature segment.
        var parts = token.Split('.');
        var sig = parts[1].ToCharArray();
        sig[0] = sig[0] == 'A' ? 'B' : 'A';
        var tampered = parts[0] + "." + new string(sig);

        service.Validate(tampered, Scope).Status.Should().Be(TokenValidationStatus.BadSignature);
    }

    [Fact]
    public void Token_signed_with_a_different_key_is_rejected()
    {
        var (mintService, _, _) = Build(MakeSecrets("first-signing-secret-that-is-long-enough!!"));
        var (token, _) = mintService.Mint("alice", Scope, TimeSpan.FromHours(1));

        var (otherService, _, _) = Build(MakeSecrets("second-signing-secret-different-length-32+"));
        otherService.Validate(token, Scope).Status.Should().Be(TokenValidationStatus.BadSignature);
    }

    [Fact]
    public void Expired_token_is_rejected()
    {
        var (service, clock, _) = Build();
        var (token, _) = service.Mint("alice", Scope, TimeSpan.FromMinutes(30));

        clock.Advance(TimeSpan.FromMinutes(31));

        service.Validate(token, Scope).Status.Should().Be(TokenValidationStatus.Expired);
    }

    [Fact]
    public void Wrong_scope_is_rejected()
    {
        var (service, _, _) = Build();
        var (token, _) = service.Mint("alice", Scope, TimeSpan.FromHours(1));

        service.Validate(token, "admin").Status.Should().Be(TokenValidationStatus.WrongScope);
    }

    [Fact]
    public void Revoked_jti_is_rejected()
    {
        var (service, _, revocations) = Build();
        var (token, claims) = service.Mint("alice", Scope, TimeSpan.FromHours(1));

        service.Validate(token, Scope).IsValid.Should().BeTrue();
        revocations.RevokeToken(claims.TokenId);

        service.Validate(token, Scope).Status.Should().Be(TokenValidationStatus.Revoked);
    }

    [Fact]
    public void Revoking_a_user_rejects_their_existing_tokens()
    {
        var (service, clock, revocations) = Build();
        var (token, _) = service.Mint("alice", Scope, TimeSpan.FromHours(1));

        revocations.RevokeUser("alice", clock.GetUtcNow());

        service.Validate(token, Scope).Status.Should().Be(TokenValidationStatus.Revoked);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-token")]
    [InlineData("only-one-segment")]
    [InlineData("bad!!.segments!!")]
    public void Malformed_tokens_are_rejected(string token)
    {
        var (service, _, _) = Build();
        service.Validate(token, Scope).Status.Should().Be(TokenValidationStatus.Malformed);
    }

    [Fact]
    public void Token_string_carries_no_secret()
    {
        var (service, _, _) = Build();
        var (token, _) = service.Mint("alice", Scope, TimeSpan.FromHours(1));

        token.Should().NotContain("sk-ant-TESTKEY-do-not-use");
        Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(token.Split('.')[0])))
            .Should().NotContain("this-is-a-32-byte-signing-secret");
    }

    private static string PadBase64(string value)
    {
        var chars = value.Replace('-', '+').Replace('_', '/');
        return chars.PadRight(chars.Length + (4 - chars.Length % 4) % 4, '=');
    }
}
