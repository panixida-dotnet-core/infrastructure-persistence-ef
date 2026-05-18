using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

internal sealed class NoSoftDeleteAggregateRoot(int id)
    : AggregateRoot<int>(id)
{
}
