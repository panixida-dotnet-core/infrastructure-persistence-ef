using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;

/// <summary>
/// Updates audit shadow properties before changes are saved by a <see cref="DbContext"/>.
/// </summary>
/// <param name="timeProvider">The time provider used to generate UTC audit timestamps.</param>
internal sealed class AuditSaveChangesInterceptor(TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    /// <summary>
    /// Updates audit properties before synchronous changes are saved.
    /// </summary>
    /// <param name="eventData">The current save operation event data.</param>
    /// <param name="result">The current interception result.</param>
    /// <returns>The save operation interception result.</returns>
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    /// <summary>
    /// Updates audit properties before asynchronous changes are saved.
    /// </summary>
    /// <param name="eventData">The current save operation event data.</param>
    /// <param name="result">The current interception result.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The save operation interception result.</returns>
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? dbContext)
    {
        if (dbContext == null)
        {
            return;
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;

        foreach (var entry in dbContext.ChangeTracker.Entries())
        {
            if (ShouldSkip(entry))
            {
                continue;
            }

            UpdateEntry(entry, now);
        }
    }

    private static void UpdateEntry(EntityEntry entry, DateTime now)
    {
        var auditProperties = GetAuditProperties(entry);

        switch (entry.State)
        {
            case EntityState.Added:
                UpdateAddedEntry(entry, auditProperties, now);
                break;

            case EntityState.Modified:
                UpdateModifiedEntry(entry, auditProperties, now);
                break;

            case EntityState.Deleted:
                UpdateDeletedEntry(entry, auditProperties, now);
                break;
        }
    }

    private static bool ShouldSkip(EntityEntry entry)
    {
        return entry.State == EntityState.Detached || entry.State == EntityState.Unchanged;
    }

    private static AuditProperties GetAuditProperties(EntityEntry entry)
    {
        return new AuditProperties(
            HasProperty(entry, EfConstants.CreatedAt),
            HasProperty(entry, EfConstants.UpdatedAt),
            HasProperty(entry, EfConstants.DeletedAt));
    }

    private static void UpdateAddedEntry(EntityEntry entry, AuditProperties auditProperties, DateTime now)
    {
        SetCurrentValue(entry, EfConstants.CreatedAt, auditProperties.HasCreatedAt, now);
        SetCurrentValue(entry, EfConstants.UpdatedAt, auditProperties.HasUpdatedAt, now);
    }

    private static void UpdateModifiedEntry(EntityEntry entry, AuditProperties auditProperties, DateTime now)
    {
        SetCurrentValue(entry, EfConstants.UpdatedAt, auditProperties.HasUpdatedAt, now);
        SetNotModified(entry, EfConstants.CreatedAt, auditProperties.HasCreatedAt);
    }

    private static void UpdateDeletedEntry(EntityEntry entry, AuditProperties auditProperties, DateTime now)
    {
        if (!auditProperties.HasDeletedAt)
        {
            return;
        }

        entry.State = EntityState.Modified;

        SetCurrentValue(entry, EfConstants.DeletedAt, auditProperties.HasDeletedAt, now);
        SetCurrentValue(entry, EfConstants.UpdatedAt, auditProperties.HasUpdatedAt, now);
        SetNotModified(entry, EfConstants.CreatedAt, auditProperties.HasCreatedAt);
    }

    private static void SetCurrentValue(EntityEntry entry, string propertyName, bool hasProperty, DateTime value)
    {
        if (!hasProperty)
        {
            return;
        }

        entry.Property(propertyName).CurrentValue = value;
    }

    private static void SetNotModified(EntityEntry entry, string propertyName, bool hasProperty)
    {
        if (!hasProperty)
        {
            return;
        }

        entry.Property(propertyName).IsModified = false;
    }

    private static bool HasProperty(EntityEntry entry, string propertyName)
    {
        return entry.Metadata.FindProperty(propertyName) != null;
    }

    private readonly record struct AuditProperties(
        bool HasCreatedAt,
        bool HasUpdatedAt,
        bool HasDeletedAt);
}
