using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Implementations;

internal sealed class AssemblyWriteRepository : IAssemblyWriteRepository
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
