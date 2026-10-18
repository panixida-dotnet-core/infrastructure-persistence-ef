using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Conventions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

/// <summary>
/// Provides the base Entity Framework Core write DbContext for aggregate persistence.
/// </summary>
/// <typeparam name="TDbContext">The concrete write DbContext type.</typeparam>
/// <param name="options">The options used by the DbContext.</param>
/// <param name="interceptors">The EF Core interceptors applied to the DbContext.</param>
public abstract class WriteDbContext<TDbContext>(
    DbContextOptions<TDbContext> options,
    IEnumerable<IInterceptor> interceptors) : DbContext(options), IAggregateTrackingContext
    where TDbContext : WriteDbContext<TDbContext>
{
    private readonly HashSet<IAggregateRoot> deletedAggregates = new(ReferenceEqualityComparer.Instance);

    IReadOnlyCollection<IAggregateRoot> IAggregateTrackingContext.DeletedAggregates => deletedAggregates;

    void IAggregateTrackingContext.ClearDeletedAggregates() => deletedAggregates.Clear();

    /// <inheritdoc />
    /// <exception cref="ConcurrencyException">The expected entity version no longer exists.</exception>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        var deletedEntries = GetDeletedAggregateEntries();
        try
        {
            var result = base.SaveChanges(acceptAllChangesOnSuccess);
            RetainDeletedAggregates(deletedEntries);
            return result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException(exception.Message, exception);
        }
    }

    /// <inheritdoc />
    /// <exception cref="ConcurrencyException">The expected entity version no longer exists.</exception>
    public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        var deletedEntries = GetDeletedAggregateEntries();
        try
        {
            var result = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
            RetainDeletedAggregates(deletedEntries);
            return result;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            throw new ConcurrencyException(exception.Message, exception);
        }
    }

    /// <summary>
    /// Gets a value indicating whether the concrete DbContext name should be used as the default database schema.
    /// </summary>
    protected virtual bool UseContextNameAsSchema { get; } = false;

    /// <summary>
    /// Gets a value indicating whether mapped table names should be converted to plural snake_case names.
    /// </summary>
    protected virtual bool UsePluralTableNames { get; } = true;

    /// <inheritdoc />
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.Conventions.Add(_ => new XminConcurrencyConvention());
    }

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

        EntityConfigurationRegistry.GetRegistration(typeof(TDbContext).Assembly)(modelBuilder);

        if (UsePluralTableNames)
        {
            modelBuilder.ApplyPluralTableNames();
        }
    }

    private EntityEntry<IAggregateRoot>[] GetDeletedAggregateEntries()
    {
        return [.. ChangeTracker.Entries<IAggregateRoot>().Where(entry => entry.State == EntityState.Deleted)];
    }

    private void RetainDeletedAggregates(IEnumerable<EntityEntry<IAggregateRoot>> entries)
    {
        foreach (var entry in entries.Where(entry => entry.State is EntityState.Deleted or EntityState.Detached))
        {
            deletedAggregates.Add(entry.Entity);
        }
    }

    private static string GetSchemaName()
    {
        return typeof(TDbContext).ToSchemaName(
            nameof(WriteDbContext<>),
            nameof(DbContext));
    }
}
