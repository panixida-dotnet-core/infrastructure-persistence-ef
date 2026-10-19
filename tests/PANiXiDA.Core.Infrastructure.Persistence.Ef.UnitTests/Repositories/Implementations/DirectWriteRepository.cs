using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal sealed class DirectWriteRepository : IRepository<TestAggregateRootId, TestAggregateRoot>
{
    public Task<TestAggregateRoot?> GetByIdAsync(
        TestAggregateRootId id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult<TestAggregateRoot?>(null);
    }

    public void Add(TestAggregateRoot aggregateRoot) { }

    public void Update(TestAggregateRoot aggregateRoot) { }

    public void Delete(TestAggregateRoot aggregateRoot) { }
}
