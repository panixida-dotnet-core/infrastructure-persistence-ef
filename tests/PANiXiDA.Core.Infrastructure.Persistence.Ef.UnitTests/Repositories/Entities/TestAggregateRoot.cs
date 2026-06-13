using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

internal sealed class TestAggregateRoot(int id) : AggregateRoot<int>(id)
{
}
