using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

internal sealed class TestAggregateRoot(int id)
    : AggregateRoot<int>(id)
{
    public string Name { get; set; } = string.Empty;
}
