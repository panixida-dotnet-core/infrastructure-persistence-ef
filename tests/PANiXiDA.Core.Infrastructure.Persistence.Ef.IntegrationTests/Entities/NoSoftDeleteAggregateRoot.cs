using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

internal readonly record struct NoSoftDeleteAggregateRootId(Guid Value) : IStronglyTypedId
{
    public static NoSoftDeleteAggregateRootId New()
    {
        return new NoSoftDeleteAggregateRootId(Guid.NewGuid());
    }
}

internal sealed class NoSoftDeleteAggregateRoot(NoSoftDeleteAggregateRootId id)
    : AggregateRoot<NoSoftDeleteAggregateRootId>(id)
{
}
