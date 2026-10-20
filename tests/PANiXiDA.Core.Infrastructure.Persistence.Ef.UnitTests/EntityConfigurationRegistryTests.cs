using System.Reflection;
using System.Reflection.Emit;
using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class EntityConfigurationRegistryTests
{
    [Fact(DisplayName = "Entity configuration registry rejects duplicate registrations")]
    public void RegisterAssembly_RejectsDuplicateAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.RunAndCollect);
        EntityConfigurationRegistry.RegisterAssembly(assembly, _ => { });

        var act = () => EntityConfigurationRegistry.RegisterAssembly(assembly, _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Entity configuration registration reports a missing generator")]
    public void GetRegistration_ThrowsWhenGeneratorIsMissing()
    {
        var act = () => EntityConfigurationRegistry.GetRegistration(typeof(string).Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Generated entity configuration registration was not found*with its analyzers*rebuild it.");
    }

    [Fact(DisplayName = "Entity configuration registration accepts an assembly without configurations")]
    public void GetRegistration_AcceptsEmptyAssembly()
    {
        var builder = new ModelBuilder();

        EntityConfigurationRegistry.GetRegistration(typeof(EntityConfigurationRegistryTests).Assembly)(builder);

        builder.Model.GetEntityTypes().Should().BeEmpty();
    }
}
