using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Implementations;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Interfaces;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ServiceCollectionExtensionsTests(PostgreSqlContainerFixture fixture)
{
    [Theory(DisplayName = "Resolving the aggregate tracker disposes each write context exactly once")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AggregateTracker_DisposesWriteContextsOnce(bool asynchronous)
    {
        var services = new ServiceCollection();
        var configuration = fixture.CreateConfiguration();
        services.AddPostgreSqlWriteEfRepository<DisposalWriteDbContext<int>>(configuration);
        services.RemoveAll<IAssemblyWriteRepository>();
        services.AddPostgreSqlWriteEfRepository<DisposalWriteDbContext<int>>(configuration);
        services.RemoveAll<IAssemblyWriteRepository>();
        services.AddPostgreSqlWriteEfRepository<DisposalWriteDbContext<string>>(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredService<DisposalWriteDbContext<int>>();
        var second = scope.ServiceProvider.GetRequiredService<DisposalWriteDbContext<string>>();
        _ = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();

        if (asynchronous)
        {
            await scope.DisposeAsync();
        }
        else
        {
            scope.Dispose();
        }

        first.DisposeCallCount.Should().Be(asynchronous ? 0 : 1);
        first.DisposeAsyncCallCount.Should().Be(asynchronous ? 1 : 0);
        second.DisposeCallCount.Should().Be(asynchronous ? 0 : 1);
        second.DisposeAsyncCallCount.Should().Be(asynchronous ? 1 : 0);
    }

    [Fact(DisplayName = "AggregateTracker collects pending deletions from multiple write contexts before saving")]
    public async Task AggregateTracker_CollectsPendingDeletionsFromAllWriteContexts()
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<TestWriteDbContext>(fixture.CreateConfiguration());
        services.RemoveAll<IAssemblyWriteRepository>();
        services.AddPostgreSqlWriteEfRepository<SchemaWriteDbContext>(fixture.CreateConfiguration());
        services.RemoveAll<IInterceptor>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var first = scope.ServiceProvider.GetRequiredService<TestWriteDbContext>();
        var second = scope.ServiceProvider.GetRequiredService<SchemaWriteDbContext>();
        await first.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        await second.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var firstAggregate = new NoSoftDeleteAggregateRoot(NoSoftDeleteAggregateRootId.New());
        var secondAggregate = new NoSoftDeleteAggregateRoot(NoSoftDeleteAggregateRootId.New());
        first.Add(firstAggregate);
        second.Add(secondAggregate);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        await second.SaveChangesAsync(TestContext.Current.CancellationToken);

        first.Remove(firstAggregate);
        second.Remove(secondAggregate);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        tracker.GetAll().Should().BeEquivalentTo([firstAggregate, secondAggregate]);
        tracker.Clear();
        first.Entry(firstAggregate).State.Should().Be(EntityState.Deleted);
        second.Entry(secondAggregate).State.Should().Be(EntityState.Deleted);
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);
        await second.SaveChangesAsync(false, TestContext.Current.CancellationToken);
        second.ChangeTracker.AcceptAllChanges();

        first.ChangeTracker.Entries().Should().BeEmpty();
        second.ChangeTracker.Entries().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
    }

    [Fact(DisplayName = "AggregateTracker uses the same scoped instances of all registered write contexts")]
    public void AggregateTracker_CollectsFromAllWriteContexts()
    {
        var services = new ServiceCollection();
        var configuration = fixture.CreateConfiguration();
        services.AddPostgreSqlWriteEfRepository<TestWriteDbContext>(configuration);
        services.RemoveAll<IAssemblyWriteRepository>();
        services.AddPostgreSqlWriteEfRepository<SchemaWriteDbContext>(configuration);
        AssertScopedRegistration<IAggregateTracker, AggregateTracker>(services);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var first = scope.ServiceProvider.GetRequiredService<TestWriteDbContext>();
        var second = scope.ServiceProvider.GetRequiredService<SchemaWriteDbContext>();
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        var firstAggregate = new TestAggregateRoot(TestAggregateRootId.New());
        var secondAggregate = new TestAggregateRoot(TestAggregateRootId.New());

        first.Add(firstAggregate);
        second.Add(secondAggregate);
        tracker.Track(secondAggregate);

        tracker.GetAll().Should().BeEquivalentTo([firstAggregate, secondAggregate]);
        scope.ServiceProvider.GetRequiredService<IAggregateTracker>().Should().BeSameAs(tracker);
        using var otherScope = provider.CreateScope();
        otherScope.ServiceProvider.GetRequiredService<IAggregateTracker>().GetAll().Should().BeEmpty();
    }

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
        scope.ServiceProvider.GetService<IUnitOfWork>().Should().BeNull();
        scope.ServiceProvider
            .GetRequiredKeyedService<IUnitOfWork>(typeof(TestWriteDbContext))
            .Should()
            .BeOfType<EfUnitOfWork<TestWriteDbContext>>();
        GetMigrationsHistorySchema<TestWriteDbContext>(scope.ServiceProvider).Should().BeNull();
        GetMigrationsHistorySchema<TestReadDbContext>(scope.ServiceProvider).Should().BeNull();
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
        scope.ServiceProvider.GetService<IUnitOfWork>().Should().BeNull();
        scope.ServiceProvider
            .GetRequiredKeyedService<IUnitOfWork>(typeof(TestWriteDbContext))
            .Should()
            .BeOfType<EfUnitOfWork<TestWriteDbContext>>();
        GetMigrationsHistorySchema<TestWriteDbContext>(scope.ServiceProvider).Should().BeNull();
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
        scope.ServiceProvider
            .GetKeyedService<IUnitOfWork>(typeof(TestReadDbContext))
            .Should()
            .BeNull();
        GetMigrationsHistorySchema<TestReadDbContext>(scope.ServiceProvider).Should().BeNull();
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

    [Fact(DisplayName = "Write registration scopes migrations history while read registration only scopes tables")]
    public void PostgreSqlRegistrations_ScopeMigrationsHistoryOnlyForWriteDbContext()
    {
        var moduleServices = new ServiceCollection();
        var secondModuleServices = new ServiceCollection();

        moduleServices.AddPostgreSqlEfRepository<SchemaWriteDbContext, SchemaReadDbContext>(
            fixture.CreateConfiguration());
        secondModuleServices.AddPostgreSqlReadEfRepository<SchemaIncludedReadDbContext>(
            fixture.CreateConfiguration());

        using var moduleProvider = moduleServices.BuildServiceProvider();
        using var moduleScope = moduleProvider.CreateScope();
        using var secondModuleProvider = secondModuleServices.BuildServiceProvider();
        using var secondModuleScope = secondModuleProvider.CreateScope();

        AssertWriteModelAndMigrationsSchema<SchemaWriteDbContext, TestAggregateRoot>(
            moduleScope.ServiceProvider,
            "schema");
        AssertReadModelSchema<SchemaReadDbContext, ProductReadDbModel>(
            moduleScope.ServiceProvider,
            "schema");
        AssertReadModelSchema<SchemaIncludedReadDbContext, ProductReadDbModel>(
            secondModuleScope.ServiceProvider,
            "schema_included");
    }

    private static void AssertWriteModelAndMigrationsSchema<TDbContext, TEntity>(
        IServiceProvider serviceProvider,
        string expectedSchema)
        where TDbContext : DbContext
    {
        var context = serviceProvider.GetRequiredService<TDbContext>();

        context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(TEntity))!
            .GetSchema()
            .Should()
            .Be(expectedSchema);
        GetMigrationsHistorySchema<TDbContext>(serviceProvider)
            .Should()
            .Be(expectedSchema);
    }

    private static void AssertReadModelSchema<TDbContext, TEntity>(
        IServiceProvider serviceProvider,
        string expectedSchema)
        where TDbContext : DbContext
    {
        var context = serviceProvider.GetRequiredService<TDbContext>();

        context.GetService<IDesignTimeModel>()
            .Model
            .FindEntityType(typeof(TEntity))!
            .GetSchema()
            .Should()
            .Be(expectedSchema);
        GetMigrationsHistorySchema<TDbContext>(serviceProvider)
            .Should()
            .BeNull();
    }

    private static string? GetMigrationsHistorySchema<TDbContext>(
        IServiceProvider serviceProvider)
        where TDbContext : DbContext
    {
        return serviceProvider
            .GetRequiredService<TDbContext>()
            .GetService<IDbContextOptions>()
            .Extensions
            .OfType<RelationalOptionsExtension>()
            .Single()
            .MigrationsHistoryTableSchema;
    }

    private static void AssertScopedRegistration<TService, TImplementation>(IServiceCollection services)
    {
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(TService)
                && descriptor.ImplementationType == typeof(TImplementation)
                && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    private sealed class DisposalWriteDbContext<TMarker>(DbContextOptions<DisposalWriteDbContext<TMarker>> options)
        : WriteDbContext<DisposalWriteDbContext<TMarker>>(options, [])
    {
        public int DisposeCallCount { get; private set; }
        public int DisposeAsyncCallCount { get; private set; }

        public override void Dispose()
        {
            DisposeCallCount++;
            base.Dispose();
        }

        public override ValueTask DisposeAsync()
        {
            DisposeAsyncCallCount++;
            return base.DisposeAsync();
        }
    }
}
