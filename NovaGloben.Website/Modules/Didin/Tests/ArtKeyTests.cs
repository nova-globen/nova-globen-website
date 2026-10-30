using Didin.Server.Controllers;

namespace Didin.Tests;

/// <summary>A poster's catalogue address is its key: only its current hash opens it without sign-in.</summary>
public sealed class ArtKeyTests
{
    private const string Hash = "0123456789abcdef0123456789abcdef";

    [Fact]
    public void The_current_hash_opens_the_poster()
    {
        ArtController.Keyed(Hash, Hash).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcdee")]
    [InlineData("0123456789ABCDEF0123456789ABCDEF")]
    [InlineData("0123456789abcdef")]
    public void Anything_else_does_not(string given)
    {
        ArtController.Keyed(Hash, given).Should().BeFalse();
    }

    [Fact]
    public void A_poster_without_a_hash_has_no_key()
    {
        ArtController.Keyed(null, Hash).Should().BeFalse();
        ArtController.Keyed("", "").Should().BeFalse();
    }
}
