using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registrations;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class RepositoryRegistrTests
{
    [Fact(DisplayName = "Generated repository registry rejects null assembly")]
    public void RegisterAssembly_RejectsNullAssembly()
    {
        var act = () => RepositoryRegistr.RegisterAssembly(null!, _ => { }, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact(DisplayName = "Generated repository registry rejects null write callback")]
    public void RegisterAssembly_RejectsNullWriteCallback()
    {
        var act = () => RepositoryRegistr.RegisterAssembly(typeof(RepositoryRegistrTests).Assembly, null!, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerWriteRepositories");
    }

    [Fact(DisplayName = "Generated repository registry rejects null read callback")]
    public void RegisterAssembly_RejectsNullReadCallback()
    {
        var act = () => RepositoryRegistr.RegisterAssembly(typeof(RepositoryRegistrTests).Assembly, _ => { }, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerReadRepositories");
    }

    [Fact(DisplayName = "Generated repository registry does not overwrite an existing assembly registration")]
    public void RegisterAssembly_RejectsDuplicateAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.RunAndCollect);
        RepositoryRegistr.RegisterAssembly(assembly, _ => { }, _ => { });

        var act = () => RepositoryRegistr.RegisterAssembly(assembly, _ => { }, _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Repository registration reports a missing generator instead of silently omitting repositories")]
    public void GetRegistration_ThrowsWhenGeneratorIsMissing()
    {
        var services = new ServiceCollection();

        var act = () => services.AddReadRepositoryImplementationsFromAssembly(typeof(string).Assembly);

        act.Should().Throw<InvalidOperationException>().WithMessage("Generated repository registration was not found*with its analyzers*rebuild it.");
        services.Should().BeEmpty();
    }

    [Fact(DisplayName = "Repository registration rejects null assembly")]
    public void GetRegistration_RejectsNullAssembly()
    {
        var nullAssembly = () => new ServiceCollection().AddReadRepositoryImplementationsFromAssembly(null!);

        nullAssembly.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }
}
