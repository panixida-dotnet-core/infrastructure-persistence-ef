using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

using Npgsql;

using Testcontainers.PostgreSql;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;

public sealed class PostgreSqlContainerFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("panixida_tests")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    public async ValueTask InitializeAsync()
    {
        await container.StartAsync(TestContext.Current.CancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await container.DisposeAsync();
    }

    public static string CreateDatabaseName()
    {
        return $"test_{Guid.NewGuid():N}";
    }

    public DbContextOptions<TDbContext> CreateOptions<TDbContext>(string databaseName)
        where TDbContext : DbContext
    {
        return new DbContextOptionsBuilder<TDbContext>()
            .UseNpgsql(GetConnectionString(databaseName))
            .Options;
    }

    public TDbContext CreateDbContext<TDbContext>(
        string databaseName,
        Func<DbContextOptions<TDbContext>, TDbContext> factory)
        where TDbContext : DbContext
    {
        return factory(CreateOptions<TDbContext>(databaseName));
    }

    public async Task<TDbContext> CreateInitializedDbContextAsync<TDbContext>(
        string databaseName,
        Func<DbContextOptions<TDbContext>, TDbContext> factory)
        where TDbContext : DbContext
    {
        var context = CreateDbContext(databaseName, factory);

        await context.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);

        return context;
    }

    public async Task<TDbContext> CreateInitializedDbContextAsync<TDbContext>(
        Func<DbContextOptions<TDbContext>, TDbContext> factory)
        where TDbContext : DbContext
    {
        return await CreateInitializedDbContextAsync(CreateDatabaseName(), factory);
    }

    public IConfiguration CreateConfiguration(string? databaseName = null)
    {
        return new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:PostgreSqlConnectionString"] =
                    GetConnectionString(databaseName ?? CreateDatabaseName())
            })
            .Build();
    }

    private string GetConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(container.GetConnectionString())
        {
            Database = databaseName
        };

        return builder.ConnectionString;
    }
}
