using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class RepositoryRegistrationExtensionsTests
{
    [Fact(DisplayName = "AddWriteRepositoryImplementationsFromAssembly registers write repository contracts")]
    public void AddWriteRepositoryImplementationsFromAssembly_RegistersWriteRepositoryContracts()
    {
        var services = new ServiceCollection();

        var result = services.AddWriteRepositoryImplementationsFromAssembly(
            typeof(RepositoryRegistrationExtensionsTests).Assembly);

        result.Should().BeSameAs(services);
        AssertScopedRegistration<IWriteRepositoryContract, WriteRepository>(services);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IRepository<int, TestAggregateRoot>)
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

    [Fact(DisplayName = "AddReadRepositoryImplementationsFromAssembly registers read repository contracts")]
    public void AddReadRepositoryImplementationsFromAssembly_RegistersReadRepositoryContracts()
    {
        var services = new ServiceCollection();

        var result = services.AddReadRepositoryImplementationsFromAssembly(
            typeof(RepositoryRegistrationExtensionsTests).Assembly);

        result.Should().BeSameAs(services);
        AssertScopedRegistration<IReadRepositoryContract, ReadRepository>(services);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IRepository<int, TestAggregateRoot>)
                || descriptor.ServiceType == typeof(IReadRepository<int>)
                || descriptor.ServiceType == typeof(IWriteRepositoryContract)
                || descriptor.ServiceType == typeof(IAbstractReadRepositoryContract)
                || descriptor.ServiceType == typeof(IGenericReadRepositoryContract)
                || descriptor.ServiceType == typeof(IUnrelatedContract));
    }

    [Fact(DisplayName = "AddWriteRepositoryImplementationsFromAssembly throws when repository contract is already registered")]
    public void AddWriteRepositoryImplementationsFromAssembly_Throws_WhenRepositoryContractIsAlreadyRegistered()
    {
        var services = new ServiceCollection();
        services.AddScoped<IWriteRepositoryContract, WriteRepository>();

        var act = () => services.AddWriteRepositoryImplementationsFromAssembly(
            typeof(RepositoryRegistrationExtensionsTests).Assembly);

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
