using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

/// <summary>
/// Provides the base Entity Framework Core read DbContext for query model persistence.
/// </summary>
/// <typeparam name="TDbContext">The concrete read DbContext type.</typeparam>
/// <param name="options">The options used by the DbContext.</param>
public abstract class ReadDbContext<TDbContext>(
    DbContextOptions<TDbContext> options) : DbContext(options)
    where TDbContext : ReadDbContext<TDbContext>
{
    internal static string SchemaName { get; } = typeof(TDbContext).ToSchemaName(
        nameof(ReadDbContext<>),
        nameof(DbContext));

    /// <summary>
    /// Gets a value indicating whether registered read models should be excluded from generated migrations.
    /// </summary>
    protected virtual bool ExcludeReadModelsFromMigrations { get; } = true;

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.RegisterReadDbModels(
            typeof(TDbContext).Assembly,
            SchemaName,
            ExcludeReadModelsFromMigrations);

        modelBuilder.ConfigureAuditableReadDbModels();
    }
}
