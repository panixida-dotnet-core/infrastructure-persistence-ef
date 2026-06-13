using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

/// <summary>
/// Tracks aggregate roots touched during the current persistence scope.
/// </summary>
public sealed class AggregateTracker : IAggregateTracker
{
    private readonly HashSet<IAggregateRoot> aggregateRoots = new(ReferenceEqualityComparer.Instance);

    /// <inheritdoc />
    public void Track(IAggregateRoot aggregateRoot)
    {
        ArgumentNullException.ThrowIfNull(aggregateRoot);

        aggregateRoots.Add(aggregateRoot);
    }

    /// <inheritdoc />
    public IReadOnlyCollection<IAggregateRoot> GetAll()
    {
        return [.. aggregateRoots];
    }

    /// <inheritdoc />
    public void Clear()
    {
        aggregateRoots.Clear();
    }
}
