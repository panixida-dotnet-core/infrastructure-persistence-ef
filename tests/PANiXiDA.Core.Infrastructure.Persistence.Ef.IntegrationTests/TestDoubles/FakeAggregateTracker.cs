using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.TestDoubles;

internal sealed class FakeAggregateTracker : IAggregateTracker
{
    private readonly List<IAggregateRoot> aggregateRoots = [];

    public void Track(IAggregateRoot aggregateRoot)
    {
        aggregateRoots.Add(aggregateRoot);
    }

    public IReadOnlyCollection<IAggregateRoot> GetAll()
    {
        return aggregateRoots;
    }

    public void Clear()
    {
        aggregateRoots.Clear();
    }
}
