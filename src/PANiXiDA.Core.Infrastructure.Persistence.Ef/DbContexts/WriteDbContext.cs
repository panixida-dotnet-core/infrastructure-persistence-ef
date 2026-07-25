using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

/// <summary>
/// Provides the base Entity Framework Core write DbContext for aggregate persistence.
/// </summary>
/// <typeparam name="TDbContext">The concrete write DbContext type.</typeparam>
/// <param name="options">The options used by the DbContext.</param>
/// <param name="interceptors">The EF Core interceptors applied to the DbContext.</param>
public abstract class WriteDbContext<TDbContext>(
    DbContextOptions<TDbContext> options,
    IEnumerable<IInterceptor> interceptors) : DbContext(options)
    where TDbContext : WriteDbContext<TDbContext>
{
    /// <summary>
    /// Gets a value indicating whether the concrete DbContext name should be used as the default database schema.
    /// </summary>
    protected virtual bool UseContextNameAsSchema { get; } = false;

    /// <summary>
    /// Gets a value indicating whether mapped table names should be converted to plural snake_case names.
    /// </summary>
    protected virtual bool UsePluralTableNames { get; } = true;

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        if (UseContextNameAsSchema)
        {
            optionsBuilder.UseNpgsql(options =>
            {
                options.MigrationsHistoryTable(
                    "__EFMigrationsHistory",
                    GetSchemaName());
            });
        }

        optionsBuilder.AddInterceptors(interceptors);
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.UseHiLo();

        if (UseContextNameAsSchema)
        {
            modelBuilder.HasDefaultSchema(GetSchemaName());
        }

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TDbContext).Assembly);

        if (UsePluralTableNames)
        {
            modelBuilder.ApplyPluralTableNames();
        }
    }

    private static string GetSchemaName()
    {
        return typeof(TDbContext).ToSchemaName(
            nameof(WriteDbContext<>),
            nameof(DbContext));
    }
}
