using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

/// <summary>
/// Collects tracked and successfully deleted aggregate roots from the scoped write DbContexts.
/// </summary>
public sealed class AggregateTracker : IAggregateTracker, IDisposable
{
    private readonly DbContext[] dbContexts;
    private readonly ConditionalWeakTable<IAggregateRoot, HashSet<Guid>> clearedEventIds = [];
    private readonly Dictionary<DbContext, EntityEntry<IAggregateRoot>[]> pendingDeletions = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IAggregateRoot> deletedAggregates = new(ReferenceEqualityComparer.Instance);
    private bool disposed;

    /// <summary>
    /// Observes saving in the specified write DbContexts without taking ownership of them.
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
            dbContext.SavingChanges += OnSavingChanges;
            dbContext.SavedChanges += OnSavedChanges;
            dbContext.SaveChangesFailed += OnSaveChangesFailed;
        }
    }

    /// <inheritdoc />
    public void Track(IAggregateRoot aggregateRoot)
    {
        var matchingContexts = dbContexts
            .Where(context => context.Model.FindRuntimeEntityType(aggregateRoot.GetType()) is not null)
            .ToArray();
        if (!matchingContexts.Any(context => context.Entry(aggregateRoot).State != EntityState.Detached)
            && !deletedAggregates.Contains(aggregateRoot))
        {
            if (matchingContexts.Length != 1)
            {
                throw new InvalidOperationException("A detached aggregate must be mapped by exactly one write DbContext. Attach it to the intended DbContext before tracking it.");
            }

            matchingContexts[0].Attach(aggregateRoot);
        }

        clearedEventIds.Remove(aggregateRoot);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IAggregateRoot> GetAll()
    {
        return [.. dbContexts
            .SelectMany(context => context.ChangeTracker.Entries())
            .Select(entry => entry.Entity)
            .OfType<IAggregateRoot>()
            .Concat(deletedAggregates)
            .Distinct<IAggregateRoot>(ReferenceEqualityComparer.Instance)
            .Where(aggregateRoot => !clearedEventIds.TryGetValue(aggregateRoot, out var eventIds)
                || aggregateRoot.GetDomainEvents().Any(domainEvent => !eventIds.Contains(domainEvent.Id)))];
    }

    /// <inheritdoc />
    public void Clear()
    {
        foreach (var aggregateRoot in GetAll())
        {
            clearedEventIds.Remove(aggregateRoot);
            clearedEventIds.Add(aggregateRoot, [.. aggregateRoot.GetDomainEvents().Select(domainEvent => domainEvent.Id)]);
        }

        pendingDeletions.Clear();
        deletedAggregates.Clear();
    }

    /// <summary>
    /// Unsubscribes from save events and releases collection state without disposing the DbContexts.
    /// </summary>
    public void Dispose()
    {
        disposed = true;
        foreach (var dbContext in dbContexts)
        {
            dbContext.SavingChanges -= OnSavingChanges;
            dbContext.SavedChanges -= OnSavedChanges;
            dbContext.SaveChangesFailed -= OnSaveChangesFailed;
        }

        clearedEventIds.Clear();
        pendingDeletions.Clear();
        deletedAggregates.Clear();
    }

    internal void CaptureSave(DbContext dbContext)
    {
        if (!disposed && !pendingDeletions.ContainsKey(dbContext))
        {
            pendingDeletions.Add(dbContext,
                [.. dbContext.ChangeTracker.Entries<IAggregateRoot>().Where(entry => entry.State == EntityState.Deleted)]);
        }
    }

    internal void DiscardSave(DbContext dbContext)
    {
        pendingDeletions.Remove(dbContext);
    }

    private void OnSavingChanges(object? sender, SavingChangesEventArgs args)
    {
        var dbContext = (DbContext)sender!;
        DiscardSave(dbContext);
        CaptureSave(dbContext);
    }

    private void OnSavedChanges(object? sender, SavedChangesEventArgs args)
    {
        if (!pendingDeletions.Remove((DbContext)sender!, out var entries))
        {
            return;
        }

        foreach (var entry in entries.Where(entry => entry.State is EntityState.Deleted or EntityState.Detached))
        {
            deletedAggregates.Add(entry.Entity);
        }
    }

    private void OnSaveChangesFailed(object? sender, SaveChangesFailedEventArgs args)
    {
        DiscardSave((DbContext)sender!);
    }
}
