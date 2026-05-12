using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

public abstract class Repository<TDbContext, TId, TAggregateRoot>(
    TDbContext dbContext,
    IAggregateTracker aggregateTracker) : IRepository<TId, TAggregateRoot>
    where TDbContext : WriteDbContext<TDbContext>
    where TId : struct
    where TAggregateRoot : AggregateRoot<TId>
{
    protected readonly DbSet<TAggregateRoot> DbSet = dbContext.Set<TAggregateRoot>();
    protected virtual IQueryable<TAggregateRoot> Query => DbSet.AsNoTracking();

    public virtual Task<TAggregateRoot?> GetByIdAsync(
        TId id,
        CancellationToken cancellationToken)
    {
        return Query
            .FirstOrDefaultAsync(item => item.Id.Equals(id), cancellationToken);
    }

    public virtual void Add(TAggregateRoot aggregateRoot)
    {
        DbSet.Add(aggregateRoot);
        aggregateTracker.Track(aggregateRoot);
    }

    public virtual void Update(TAggregateRoot aggregateRoot)
    {
        DbSet.Update(aggregateRoot);
        aggregateTracker.Track(aggregateRoot);
    }

    public virtual void Delete(TAggregateRoot aggregateRoot)
    {
        DbSet.Remove(aggregateRoot);
        aggregateTracker.Track(aggregateRoot);
    }
}
