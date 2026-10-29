using System.Reflection;

namespace Delato.Tests;

public class ModuleSwitchTests
{
    [Fact]
    public void The_server_assembly_is_switched_by_the_id_delato()
    {
        var server = Assembly.Load("Delato.Server.Stomme");

        var switches = server.GetCustomAttributesData()
            .Where(a => a.AttributeType.Name == "ModuleSwitchAttribute")
            .ToList();

        switches.Should().ContainSingle();
        switches[0].ConstructorArguments.Should().ContainSingle()
            .Which.Value.Should().Be("delato");
    }

    [Fact]
    public void The_assemblies_carry_the_names_the_host_scans_for()
    {
        typeof(Delato.Shared.Domain.SplitCalculator).Assembly.GetName().Name.Should().Be("Delato.Shared.Stomme");
        Assembly.Load("Delato.Server.Stomme").GetName().Name.Should().Be("Delato.Server.Stomme");
    }
}
