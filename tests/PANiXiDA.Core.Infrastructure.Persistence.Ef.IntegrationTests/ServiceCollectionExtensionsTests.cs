using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Implementations;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Interfaces;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ServiceCollectionExtensionsTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "AddPostgreSqlEfRepository registers write and read infrastructure and repositories")]
    public void AddPostgreSqlEfRepository_RegistersWriteAndReadInfrastructureAndRepositories()
    {
        var services = new ServiceCollection();
        var configuration = fixture.CreateConfiguration();

        var result = services.AddPostgreSqlEfRepository<TestWriteDbContext, TestReadDbContext>(configuration);

        result.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TestWriteDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<TestReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork<TestWriteDbContext>>();
        scope.ServiceProvider.GetRequiredService<IAggregateTracker>().Should().BeOfType<AggregateTracker>();
        scope.ServiceProvider.GetRequiredService<TimeProvider>().Should().Be(TimeProvider.System);
        scope.ServiceProvider.GetServices<IInterceptor>()
            .Should()
            .ContainSingle(interceptor => interceptor is AuditSaveChangesInterceptor);
        AssertScopedRegistration<IAssemblyWriteRepository, AssemblyWriteRepository>(services);
        AssertScopedRegistration<IAssemblyReadRepository, AssemblyReadRepository>(services);
    }

    [Fact(DisplayName = "AddPostgreSqlEfRepository throws when repository contract is already registered")]
    public void AddPostgreSqlEfRepository_Throws_WhenRepositoryContractIsAlreadyRegistered()
    {
        var services = new ServiceCollection();
        var configuration = fixture.CreateConfiguration();
        services.AddScoped<IAssemblyWriteRepository, AssemblyWriteRepository>();

        var act = () => services.AddPostgreSqlEfRepository<TestWriteDbContext, TestReadDbContext>(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                $"Repository interface '{typeof(IAssemblyWriteRepository).FullName}' is already registered. " +
                $"Conflicting implementation: '{typeof(AssemblyWriteRepository).FullName}'.");
    }

    [Fact(DisplayName = "AddPostgreSqlWriteEfRepository registers only write infrastructure and repositories")]
    public void AddPostgreSqlWriteEfRepository_RegistersOnlyWriteInfrastructureAndRepositories()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlWriteEfRepository<TestWriteDbContext>(fixture.CreateConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TestWriteDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<TestReadDbContext>().Should().BeNull();
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork<TestWriteDbContext>>();
        scope.ServiceProvider.GetRequiredService<IAggregateTracker>().Should().BeOfType<AggregateTracker>();
        AssertScopedRegistration<IAssemblyWriteRepository, AssemblyWriteRepository>(services);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IAssemblyReadRepository));
    }

    [Fact(DisplayName = "AddPostgreSqlReadEfRepository registers only read infrastructure and repositories")]
    public void AddPostgreSqlReadEfRepository_RegistersOnlyReadInfrastructureAndRepositories()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlReadEfRepository<TestReadDbContext>(fixture.CreateConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TestReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<TestWriteDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<IUnitOfWork>().Should().BeNull();
        scope.ServiceProvider.GetService<IAggregateTracker>().Should().BeNull();
        AssertScopedRegistration<IAssemblyReadRepository, AssemblyReadRepository>(services);
        services.Should().NotContain(descriptor =>
            descriptor.ServiceType == typeof(IAssemblyWriteRepository));
    }

    [Fact(DisplayName = "AddPostgreSqlEfRepository throws when connection string is missing")]
    public void AddPostgreSqlEfRepository_Throws_WhenConnectionStringIsMissing()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();

        var act = () => services.AddPostgreSqlEfRepository<TestWriteDbContext, TestReadDbContext>(configuration);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"Connection string '{EfConstants.PostgreSqlConnectionStringName}' not found.");
    }

    private static void AssertScopedRegistration<TService, TImplementation>(IServiceCollection services)
    {
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(TService)
                && descriptor.ImplementationType == typeof(TImplementation)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
