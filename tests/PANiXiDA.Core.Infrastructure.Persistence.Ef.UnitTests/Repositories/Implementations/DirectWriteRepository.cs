using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal sealed class DirectWriteRepository : IRepository<int, TestAggregateRoot>
{
    public Task<TestAggregateRoot?> GetByIdAsync(
        int id,
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
