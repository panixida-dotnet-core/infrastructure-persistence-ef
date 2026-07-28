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

    public Task AddAsync(
        TestAggregateRoot aggregateRoot,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        TestAggregateRoot aggregateRoot,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    public Task DeleteAsync(
        TestAggregateRoot aggregateRoot,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
