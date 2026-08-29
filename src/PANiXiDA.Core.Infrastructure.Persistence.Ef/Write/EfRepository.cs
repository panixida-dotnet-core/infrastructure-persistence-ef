using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Provides a base EF Core repository for aggregate roots.
/// </summary>
/// <typeparam name="TDbContext">The write DbContext type.</typeparam>
/// <typeparam name="TId">The aggregate root identifier type.</typeparam>
/// <typeparam name="TAggregateRoot">The aggregate root type.</typeparam>
/// <param name="dbContext">The DbContext used by the repository.</param>
/// <param name="aggregateTracker">The tracker used to collect touched aggregate roots.</param>
public abstract class EfRepository<TDbContext, TId, TAggregateRoot>(
    TDbContext dbContext,
    IAggregateTracker aggregateTracker) : IRepository<TId, TAggregateRoot>
    where TDbContext : WriteDbContext<TDbContext>
    where TId : struct, IStronglyTypedId
    where TAggregateRoot : AggregateRoot<TId>
{
    /// <summary>
    /// Gets the EF Core set used to write aggregate roots.
    /// </summary>
    protected readonly DbSet<TAggregateRoot> DbSet = dbContext.Set<TAggregateRoot>();

    /// <summary>
    /// Gets the base read query used by repository lookup operations.
    /// </summary>
    protected virtual IQueryable<TAggregateRoot> Query => DbSet.AsNoTracking();

    /// <inheritdoc />
    public virtual Task<TAggregateRoot?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken)
    {
        return Query
            .FirstOrDefaultAsync(item => item.Id.Equals(id), cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task AddAsync(
        TAggregateRoot aggregateRoot,
        CancellationToken cancellationToken)
    {
        DbSet.Add(aggregateRoot);
        aggregateTracker.Track(aggregateRoot);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task UpdateAsync(
        TAggregateRoot aggregateRoot,
        CancellationToken cancellationToken)
    {
        DbSet.Update(aggregateRoot);
        aggregateTracker.Track(aggregateRoot);
        return dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task DeleteAsync(
        TAggregateRoot aggregateRoot,
        CancellationToken cancellationToken)
    {
        DbSet.Remove(aggregateRoot);
        aggregateTracker.Track(aggregateRoot);
        return dbContext.SaveChangesAsync(cancellationToken);
    }
}
