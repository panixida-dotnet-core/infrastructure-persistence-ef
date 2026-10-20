using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Provides a base EF Core repository for aggregate roots.
/// </summary>
/// <typeparam name="TDbContext">The write DbContext type.</typeparam>
/// <typeparam name="TId">The aggregate root identifier type.</typeparam>
/// <typeparam name="TAggregateRoot">The aggregate root type.</typeparam>
/// <param name="dbContext">The DbContext used by the repository.</param>
public abstract class EfRepository<TDbContext, TId,
    [DynamicallyAccessedMembers(TrimmingConstants.EntityMembers)] TAggregateRoot>(
    TDbContext dbContext) : IRepository<TId, TAggregateRoot>
    where TDbContext : WriteDbContext<TDbContext>
    where TId : struct, IStronglyTypedId
    where TAggregateRoot : AggregateRoot<TId>
{
    /// <summary>
    /// Gets the EF Core set used to write aggregate roots.
    /// </summary>
    protected readonly DbSet<TAggregateRoot> DbSet = dbContext.Set<TAggregateRoot>();

    /// <summary>
    /// Gets the tracking query used to load aggregate roots for modification.
    /// </summary>
    protected virtual IQueryable<TAggregateRoot> Query => DbSet.AsTracking();

    /// <inheritdoc />
    public virtual Task<TAggregateRoot?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken)
    {
        return Query
            .FirstOrDefaultAsync(item => item.Id.Equals(id), cancellationToken);
    }

    /// <inheritdoc />
    public virtual void Add(TAggregateRoot aggregateRoot)
    {
        DbSet.Add(aggregateRoot);
    }

    /// <inheritdoc />
    public virtual void Update(TAggregateRoot aggregateRoot)
    {
        DbSet.Update(aggregateRoot);
    }

    /// <inheritdoc />
    public virtual void Delete(TAggregateRoot aggregateRoot)
    {
        DbSet.Remove(aggregateRoot);
    }
}
