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
    /// <summary>
    /// Gets a value indicating whether the concrete DbContext name should be used as the default database schema.
    /// </summary>
    protected virtual bool UseContextNameAsSchema { get; } = false;

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

        var schemaName = UseContextNameAsSchema
            ? GetSchemaName()
            : null;

        modelBuilder.RegisterReadDbModels(
            typeof(TDbContext).Assembly,
            schemaName,
            ExcludeReadModelsFromMigrations);

        modelBuilder.ConfigureAuditableReadDbModels();
    }

    private static string GetSchemaName()
    {
        return typeof(TDbContext).ToSchemaName(
            nameof(ReadDbContext<>),
            nameof(DbContext));
    }
}
