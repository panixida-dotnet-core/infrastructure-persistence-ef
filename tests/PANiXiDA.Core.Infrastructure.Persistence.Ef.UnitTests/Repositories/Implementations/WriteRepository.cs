using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal sealed class WriteRepository : IWriteRepositoryContract
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
