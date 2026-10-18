using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;

internal sealed class AggregateTrackingInterceptor(Func<IAggregateTracker> resolveTracker) : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        CaptureSave(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        CaptureSave(eventData);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesCanceled(DbContextEventData eventData)
    {
        DiscardSave(eventData);
    }

    public override Task SaveChangesCanceledAsync(DbContextEventData eventData, CancellationToken cancellationToken = default)
    {
        DiscardSave(eventData);
        return Task.CompletedTask;
    }

    private void CaptureSave(DbContextEventData eventData)
    {
        if (eventData.Context is { } context && resolveTracker() is AggregateTracker tracker)
        {
            tracker.CaptureSave(context);
        }
    }

    private void DiscardSave(DbContextEventData eventData)
    {
        if (eventData.Context is { } context && resolveTracker() is AggregateTracker tracker)
        {
            tracker.DiscardSave(context);
        }
    }
}
