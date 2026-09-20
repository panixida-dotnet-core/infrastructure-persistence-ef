using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class RepositoryRegistryTests
{
    [Fact(DisplayName = "Generated repository registry rejects null assembly")]
    public void RegisterAssembly_RejectsNullAssembly()
    {
        var act = () => RepositoryRegistry.RegisterAssembly(null!, _ => { }, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact(DisplayName = "Generated repository registry rejects null write callback")]
    public void RegisterAssembly_RejectsNullWriteCallback()
    {
        var act = () => RepositoryRegistry.RegisterAssembly(typeof(RepositoryRegistryTests).Assembly, null!, _ => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerWriteRepositories");
    }

    [Fact(DisplayName = "Generated repository registry rejects null read callback")]
    public void RegisterAssembly_RejectsNullReadCallback()
    {
        var act = () => RepositoryRegistry.RegisterAssembly(typeof(RepositoryRegistryTests).Assembly, _ => { }, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerReadRepositories");
    }

    [Fact(DisplayName = "Generated repository registry does not overwrite an existing assembly registration")]
    public void RegisterAssembly_RejectsDuplicateAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.RunAndCollect);
        RepositoryRegistry.RegisterAssembly(assembly, _ => { }, _ => { });

        var act = () => RepositoryRegistry.RegisterAssembly(assembly, _ => { }, _ => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Repository registration reports a missing generator instead of silently omitting repositories")]
    public void GetRegistration_ThrowsWhenGeneratorIsMissing()
    {
        var services = new ServiceCollection();

        var act = () => RepositoryRegistry.GetRegistration(typeof(string).Assembly).RegisterReadRepositories(services);

        act.Should().Throw<InvalidOperationException>().WithMessage("Generated repository registration was not found*with its analyzers*rebuild it.");
        services.Should().BeEmpty();
    }

    [Fact(DisplayName = "Repository registration rejects null assembly")]
    public void GetRegistration_RejectsNullAssembly()
    {
        var nullAssembly = () => RepositoryRegistry.GetRegistration(null!);

        nullAssembly.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact(DisplayName = "Generated write registration registers write repository contracts")]
    public void RegisterWriteRepositories_RegistersWriteRepositoryContracts()
    {
        var services = new ServiceCollection();

        RepositoryRegistry.GetRegistration(typeof(RepositoryRegistryTests).Assembly).RegisterWriteRepositories(services);

        AssertScopedRegistration<IWriteRepositoryContract, WriteRepository>(services);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IRepository<TestAggregateRootId, TestAggregateRoot>)
                || descriptor.ServiceType == typeof(IReadRepository<int>)
                || descriptor.ServiceType == typeof(IReadRepositoryContract)
                || descriptor.ServiceType == typeof(IAbstractReadRepositoryContract)
                || descriptor.ServiceType == typeof(IGenericReadRepositoryContract)
                || descriptor.ServiceType == typeof(IUnrelatedContract));
        AssertImplementationNotRegistered<DirectWriteRepository>(services);
        AssertImplementationNotRegistered<DirectReadRepository>(services);
        AssertImplementationNotRegistered<AbstractReadRepository>(services);
        AssertImplementationNotRegistered(typeof(GenericReadRepository<>), services);
        AssertImplementationNotRegistered<UnrelatedRepository>(services);
    }

    [Fact(DisplayName = "Generated read registration registers read repository contracts")]
    public void RegisterReadRepositories_RegistersReadRepositoryContracts()
    {
        var services = new ServiceCollection();

        RepositoryRegistry.GetRegistration(typeof(RepositoryRegistryTests).Assembly).RegisterReadRepositories(services);

        AssertScopedRegistration<IReadRepositoryContract, ReadRepository>(services);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IRepository<TestAggregateRootId, TestAggregateRoot>)
                || descriptor.ServiceType == typeof(IReadRepository<int>)
                || descriptor.ServiceType == typeof(IWriteRepositoryContract)
                || descriptor.ServiceType == typeof(IAbstractReadRepositoryContract)
                || descriptor.ServiceType == typeof(IGenericReadRepositoryContract)
                || descriptor.ServiceType == typeof(IUnrelatedContract));
    }

    [Fact(DisplayName = "Generated write registration throws when a repository contract is already registered")]
    public void RegisterWriteRepositories_Throws_WhenRepositoryContractIsAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWriteRepositoryContract, WriteRepository>();

        var act = () => RepositoryRegistry.GetRegistration(typeof(RepositoryRegistryTests).Assembly).RegisterWriteRepositories(services);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"Repository interface '{typeof(IWriteRepositoryContract).FullName}' is already registered. " +
                $"Conflicting implementation: '{typeof(WriteRepository).FullName}'.");
    }

    private static void AssertScopedRegistration<TService, TImplementation>(IServiceCollection services)
    {
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(TService)
                && descriptor.ImplementationType == typeof(TImplementation)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private static void AssertImplementationNotRegistered<TImplementation>(IServiceCollection services)
    {
        AssertImplementationNotRegistered(typeof(TImplementation), services);
    }

    private static void AssertImplementationNotRegistered(
        Type implementationType,
        IServiceCollection services)
    {
        services.Should().NotContain(descriptor =>
            descriptor.ImplementationType == implementationType);
    }
}
