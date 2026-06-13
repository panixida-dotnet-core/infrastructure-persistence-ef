using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;

/// <summary>
/// Provides extension methods for registering EF Core persistence infrastructure in a dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers PostgreSQL write and read DbContexts with EF Core persistence infrastructure and repository implementations.
    /// </summary>
    /// <typeparam name="TWriteDbContext">The write DbContext type.</typeparam>
    /// <typeparam name="TReadDbContext">The read DbContext type.</typeparam>
    /// <param name="serviceCollection">The service collection to register services into.</param>
    /// <param name="configuration">The application configuration that contains the PostgreSQL connection string.</param>
    /// <returns>The same service collection after registration.</returns>
    public static IServiceCollection AddPostgreSqlEfRepository<TWriteDbContext, TReadDbContext>(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
        where TWriteDbContext : WriteDbContext<TWriteDbContext>
        where TReadDbContext : ReadDbContext<TReadDbContext>
    {
        var connectionString = GetPostgreSqlConnectionString(configuration);

        RegisterWriteDbContext<TWriteDbContext>(serviceCollection, connectionString);
        RegisterReadDbContext<TReadDbContext>(serviceCollection, connectionString);

        RegisterWriteEfInfrastructure<TWriteDbContext>(serviceCollection);
        serviceCollection.AddWriteRepositoryImplementationsFromAssembly(typeof(TWriteDbContext).Assembly);
        serviceCollection.AddReadRepositoryImplementationsFromAssembly(typeof(TReadDbContext).Assembly);

        return serviceCollection;
    }

    /// <summary>
    /// Registers only the PostgreSQL write DbContext, EF Core write infrastructure, and write repository implementations.
    /// </summary>
    /// <typeparam name="TWriteDbContext">The write DbContext type.</typeparam>
    /// <param name="serviceCollection">The service collection to register services into.</param>
    /// <param name="configuration">The application configuration that contains the PostgreSQL connection string.</param>
    /// <returns>The same service collection after registration.</returns>
    public static IServiceCollection AddPostgreSqlWriteEfRepository<TWriteDbContext>(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
        where TWriteDbContext : WriteDbContext<TWriteDbContext>
    {
        var connectionString = GetPostgreSqlConnectionString(configuration);

        RegisterWriteDbContext<TWriteDbContext>(serviceCollection, connectionString);
        RegisterWriteEfInfrastructure<TWriteDbContext>(serviceCollection);
        serviceCollection.AddWriteRepositoryImplementationsFromAssembly(typeof(TWriteDbContext).Assembly);

        return serviceCollection;
    }

    /// <summary>
    /// Registers only the PostgreSQL read DbContext and read repository implementations.
    /// </summary>
    /// <typeparam name="TReadDbContext">The read DbContext type.</typeparam>
    /// <param name="serviceCollection">The service collection to register services into.</param>
    /// <param name="configuration">The application configuration that contains the PostgreSQL connection string.</param>
    /// <returns>The same service collection after registration.</returns>
    public static IServiceCollection AddPostgreSqlReadEfRepository<TReadDbContext>(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
        where TReadDbContext : ReadDbContext<TReadDbContext>
    {
        var connectionString = GetPostgreSqlConnectionString(configuration);

        RegisterReadDbContext<TReadDbContext>(serviceCollection, connectionString);
        serviceCollection.AddReadRepositoryImplementationsFromAssembly(typeof(TReadDbContext).Assembly);

        return serviceCollection;
    }

    private static void RegisterWriteDbContext<TWriteDbContext>(
        IServiceCollection serviceCollection,
        string connectionString)
        where TWriteDbContext : WriteDbContext<TWriteDbContext>
    {
        serviceCollection.AddDbContext<TWriteDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
        });
    }

    private static void RegisterReadDbContext<TReadDbContext>(
        IServiceCollection serviceCollection,
        string connectionString)
        where TReadDbContext : ReadDbContext<TReadDbContext>
    {
        serviceCollection.AddDbContext<TReadDbContext>(options =>
        {
            options.UseNpgsql(connectionString);
            options.UseSnakeCaseNamingConvention();
        });
    }

    private static void RegisterWriteEfInfrastructure<TWriteDbContext>(
        IServiceCollection serviceCollection)
        where TWriteDbContext : WriteDbContext<TWriteDbContext>
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);

        serviceCollection.TryAddEnumerable(
            ServiceDescriptor.Scoped<IInterceptor, AuditSaveChangesInterceptor>());

        serviceCollection.TryAddScoped<IAggregateTracker, AggregateTracker>();
        serviceCollection.AddScoped<IUnitOfWork, EfUnitOfWork<TWriteDbContext>>();
    }

    private static string GetPostgreSqlConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString(EfConstants.PostgreSqlConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{EfConstants.PostgreSqlConnectionStringName}' not found.");
    }
}
