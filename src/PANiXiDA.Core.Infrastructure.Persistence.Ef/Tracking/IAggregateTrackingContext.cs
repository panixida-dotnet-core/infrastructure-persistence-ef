using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

internal interface IAggregateTrackingContext
{
    IReadOnlyCollection<IAggregateRoot> DeletedAggregates { get; }

    void ClearDeletedAggregates();
}
