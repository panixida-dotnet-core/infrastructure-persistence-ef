using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ServiceCollectionExtensionsTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "AddPostgreSqlEfRepository registers write and read infrastructure")]
    public void AddPostgreSqlEfRepository_RegistersWriteAndReadInfrastructure()
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
        scope.ServiceProvider.GetRequiredService<TimeProvider>().Should().Be(TimeProvider.System);
        scope.ServiceProvider.GetServices<IInterceptor>()
            .Should()
            .ContainSingle(interceptor => interceptor is AuditSaveChangesInterceptor);
    }

    [Fact(DisplayName = "AddPostgreSqlWriteEfRepository registers only write infrastructure")]
    public void AddPostgreSqlWriteEfRepository_RegistersOnlyWriteInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlWriteEfRepository<TestWriteDbContext>(fixture.CreateConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TestWriteDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<TestReadDbContext>().Should().BeNull();
        scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Should().BeOfType<EfUnitOfWork<TestWriteDbContext>>();
    }

    [Fact(DisplayName = "AddPostgreSqlReadEfRepository registers only read infrastructure")]
    public void AddPostgreSqlReadEfRepository_RegistersOnlyReadInfrastructure()
    {
        var services = new ServiceCollection();

        services.AddPostgreSqlReadEfRepository<TestReadDbContext>(fixture.CreateConfiguration());

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<TestReadDbContext>().Should().NotBeNull();
        scope.ServiceProvider.GetService<TestWriteDbContext>().Should().BeNull();
        scope.ServiceProvider.GetService<IUnitOfWork>().Should().BeNull();
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

}
