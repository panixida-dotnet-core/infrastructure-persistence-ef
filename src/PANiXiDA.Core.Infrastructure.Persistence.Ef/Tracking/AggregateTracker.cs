using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

/// <summary>
/// Collects aggregate roots from the write DbContexts in the current persistence scope.
/// </summary>
public sealed class AggregateTracker : IAggregateTracker, IDisposable
{
    private static readonly EventHandler<EntityStateChangedEventArgs> StateChangedHandler = new(OnStateChanged);
    private readonly DbContext[] dbContexts;
    private readonly ConditionalWeakTable<IAggregateRoot, object> ignoredAggregateRoots = [];

    /// <summary>
    /// Initializes a tracker for the specified write DbContexts without taking ownership of them.
    /// </summary>
    /// <param name="dbContexts">The write DbContexts participating in this persistence scope.</param>
    public AggregateTracker(IEnumerable<DbContext> dbContexts)
    {
        this.dbContexts = [.. dbContexts.Distinct<DbContext>(ReferenceEqualityComparer.Instance)];
        if (this.dbContexts.Any(dbContext => dbContext is null))
        {
            throw new ArgumentException("Write DbContexts cannot contain null entries.", nameof(dbContexts));
        }

        foreach (var dbContext in this.dbContexts)
        {
            dbContext.ChangeTracker.StateChanged += StateChangedHandler;
        }
    }

    /// <inheritdoc />
    public void Track(IAggregateRoot aggregateRoot)
    {
        var matchingContexts = dbContexts
            .Where(context => context.Model.FindRuntimeEntityType(aggregateRoot.GetType()) is not null)
            .ToArray();
        if (!matchingContexts.Any(context => context.Entry(aggregateRoot).State != EntityState.Detached)
            && !dbContexts.Any(context => DeletedAggregateStore.Get(context).Contains(aggregateRoot)))
        {
            if (matchingContexts.Length != 1)
            {
                throw new InvalidOperationException("A detached aggregate must be mapped by exactly one write DbContext. Attach it to the intended DbContext before tracking it.");
            }

            matchingContexts[0].Attach(aggregateRoot);
        }

        ignoredAggregateRoots.Remove(aggregateRoot);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IAggregateRoot> GetAll()
    {
        return [.. dbContexts
            .SelectMany(context => context.ChangeTracker.Entries())
            .Select(entry => entry.Entity)
            .OfType<IAggregateRoot>()
            .Concat(dbContexts.SelectMany(DeletedAggregateStore.Get))
            .Distinct<IAggregateRoot>(ReferenceEqualityComparer.Instance)
            .Where(aggregateRoot => !ignoredAggregateRoots.TryGetValue(aggregateRoot, out _))];
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (var aggregateRoot in GetAll())
        {
            ignoredAggregateRoots.GetValue(aggregateRoot, _ => new object());
        }

        ClearDeletedAggregates();
    }

    /// <summary>
    /// Unsubscribes from EF tracking events without disposing the DbContexts.
    /// </summary>
    public void Dispose()
    {
        foreach (var dbContext in dbContexts)
        {
            dbContext.ChangeTracker.StateChanged -= StateChangedHandler;
        }
        ignoredAggregateRoots.Clear();
        ClearDeletedAggregates();
    }

    private void ClearDeletedAggregates()
    {
        foreach (var dbContext in dbContexts)
        {
            DeletedAggregateStore.Get(dbContext).Clear();
        }
    }

    private static void OnStateChanged(object? sender, EntityStateChangedEventArgs args)
    {
        if (args.OldState == EntityState.Deleted
            && args.NewState == EntityState.Detached
            && args.Entry.Entity is IAggregateRoot aggregateRoot)
        {
            DeletedAggregateStore.Get(args.Entry.Context).Add(aggregateRoot);
        }
    }
}
