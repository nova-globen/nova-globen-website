using Delato.Shared.Domain;
using Xunit;

namespace Delato.Tests.Domain;

public class SmokeTests
{
    [Fact]
    public void Round2_IsHalfUp()
    {
        Assert.Equal(0.13m, Money.Round2(0.125m));
        Assert.Equal(131.35m, Money.Round2(120.50m * 1.09m));
    }
}
