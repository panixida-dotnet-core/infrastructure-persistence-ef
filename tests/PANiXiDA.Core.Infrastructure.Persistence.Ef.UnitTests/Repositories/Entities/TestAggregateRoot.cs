using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

internal readonly record struct TestAggregateRootId(Guid Value) : IStronglyTypedId
{
    public static TestAggregateRootId New()
    {
        return new TestAggregateRootId(Guid.NewGuid());
    }
}

internal sealed class TestAggregateRoot(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
{
}
