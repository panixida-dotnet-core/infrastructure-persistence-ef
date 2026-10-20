using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

/// <summary>
/// Collects aggregate roots from the write DbContexts in the current persistence scope.
/// </summary>
public sealed class AggregateTracker : IAggregateTracker, IDisposable
{
    private readonly DbContext[] dbContexts;
    private readonly ConditionalWeakTable<IAggregateRoot, HashSet<Guid>> clearedEventIds = [];

    /// <summary>
    /// Resolves the write DbContexts for this persistence scope without taking ownership of them.
    /// </summary>
    /// <param name="dbContextFactories">Factories that return the existing scoped write DbContexts.</param>
    public AggregateTracker(IEnumerable<Func<DbContext>> dbContextFactories)
    {
        dbContexts = [.. dbContextFactories
            .Select(resolve => resolve())
            .Distinct<DbContext>(ReferenceEqualityComparer.Instance)];
        if (dbContexts.Any(dbContext => dbContext is null))
        {
            throw new ArgumentException("Write DbContexts cannot contain null entries.", nameof(dbContextFactories));
        }
    }

    /// <inheritdoc />
    public void Track(IAggregateRoot aggregateRoot)
    {
        var matchingContexts = dbContexts
            .Where(context => context.Model.FindRuntimeEntityType(aggregateRoot.GetType()) is not null)
            .ToArray();
        if (!matchingContexts.Any(context => context.Entry(aggregateRoot).State != EntityState.Detached))
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
    }

    /// <summary>
    /// Releases the event collection state without disposing the DbContexts.
    /// </summary>
    public void Dispose()
    {
        clearedEventIds.Clear();
    }
}
