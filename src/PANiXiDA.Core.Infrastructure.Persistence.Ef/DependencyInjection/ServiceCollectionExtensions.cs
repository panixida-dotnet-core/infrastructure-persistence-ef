using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;

/// <summary>
/// Содержит расширения для регистрации EF Core инфраструктуры в контейнере зависимостей.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Регистрирует PostgreSQL write/read DbContext и EF-инфраструктуру.
    /// </summary>
    /// <typeparam name="TWriteDbContext">Тип write DbContext.</typeparam>
    /// <typeparam name="TReadDbContext">Тип read DbContext.</typeparam>
    /// <param name="serviceCollection">Коллекция сервисов для регистрации.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Та же коллекция сервисов после регистрации.</returns>
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

        return serviceCollection;
    }

    /// <summary>
    /// Регистрирует только PostgreSQL write DbContext и EF-инфраструктуру.
    /// </summary>
    /// <typeparam name="TWriteDbContext">Тип write DbContext.</typeparam>
    /// <param name="serviceCollection">Коллекция сервисов для регистрации.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Та же коллекция сервисов после регистрации.</returns>
    public static IServiceCollection AddPostgreSqlWriteEfRepository<TWriteDbContext>(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
        where TWriteDbContext : WriteDbContext<TWriteDbContext>
    {
        var connectionString = GetPostgreSqlConnectionString(configuration);

        RegisterWriteDbContext<TWriteDbContext>(serviceCollection, connectionString);
        RegisterWriteEfInfrastructure<TWriteDbContext>(serviceCollection);

        return serviceCollection;
    }

    /// <summary>
    /// Регистрирует только PostgreSQL read DbContext.
    /// </summary>
    /// <typeparam name="TReadDbContext">Тип read DbContext.</typeparam>
    /// <param name="serviceCollection">Коллекция сервисов для регистрации.</param>
    /// <param name="configuration">Конфигурация приложения.</param>
    /// <returns>Та же коллекция сервисов после регистрации.</returns>
    public static IServiceCollection AddPostgreSqlReadEfRepository<TReadDbContext>(
        this IServiceCollection serviceCollection,
        IConfiguration configuration)
        where TReadDbContext : ReadDbContext<TReadDbContext>
    {
        var connectionString = GetPostgreSqlConnectionString(configuration);

        RegisterReadDbContext<TReadDbContext>(serviceCollection, connectionString);

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

        serviceCollection.AddScoped<IUnitOfWork, UnitOfWork<TWriteDbContext>>();
    }

    private static string GetPostgreSqlConnectionString(IConfiguration configuration)
    {
        return configuration.GetConnectionString(EfConstants.PostgreSqlConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{EfConstants.PostgreSqlConnectionStringName}' not found.");
    }
}
