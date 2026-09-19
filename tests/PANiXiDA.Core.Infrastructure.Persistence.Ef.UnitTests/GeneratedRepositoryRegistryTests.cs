using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class GeneratedRepositoryRegistryTests
{
    [Fact(DisplayName = "Generated repository registry rejects null assembly")]
    public void RegisterAssembly_RejectsNullAssembly()
    {
        var act = () => GeneratedRepositoryRegistry.RegisterAssembly(null!, _ => { }, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact(DisplayName = "Generated repository registry rejects null write callback")]
    public void RegisterAssembly_RejectsNullWriteCallback()
    {
        var act = () => GeneratedRepositoryRegistry.RegisterAssembly(typeof(GeneratedRepositoryRegistryTests).Assembly, null!, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerWriteRepositories");
    }

    [Fact(DisplayName = "Generated repository registry rejects null read callback")]
    public void RegisterAssembly_RejectsNullReadCallback()
    {
        var act = () => GeneratedRepositoryRegistry.RegisterAssembly(typeof(GeneratedRepositoryRegistryTests).Assembly, _ => { }, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerReadRepositories");
    }

    [Fact(DisplayName = "Generated repository registry does not overwrite an existing assembly registration")]
    public void RegisterAssembly_RejectsDuplicateAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.RunAndCollect);
        GeneratedRepositoryRegistry.RegisterAssembly(assembly, _ => { }, _ => { });

        var act = () => GeneratedRepositoryRegistry.RegisterAssembly(assembly, _ => { }, _ => { });

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

    [Fact(DisplayName = "Repository registration rejects null arguments")]
    public void GetRegistration_RejectsNullArguments()
    {
        var nullAssembly = () => new ServiceCollection().AddReadRepositoryImplementationsFromAssembly(null!);
        var nullReadServices = () => RepositoryRegistrationExtensions.AddReadRepositoryImplementationsFromAssembly(null!, typeof(string).Assembly);
        var nullWriteServices = () => RepositoryRegistrationExtensions.AddWriteRepositoryImplementationsFromAssembly(null!, typeof(string).Assembly);

        nullAssembly.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
        nullReadServices.Should().Throw<ArgumentNullException>().WithParameterName("serviceCollection");
        nullWriteServices.Should().Throw<ArgumentNullException>().WithParameterName("serviceCollection");
    }
}
