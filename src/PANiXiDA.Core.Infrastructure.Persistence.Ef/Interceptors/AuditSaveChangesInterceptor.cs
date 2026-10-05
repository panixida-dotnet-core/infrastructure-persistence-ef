using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;

/// <summary>
/// Updates audit properties before changes are saved by a <see cref="DbContext"/>.
/// </summary>
/// <param name="timeProvider">The time provider used to generate UTC audit timestamps.</param>
internal sealed class AuditSaveChangesInterceptor(TimeProvider timeProvider)
    : SaveChangesInterceptor
{
    private static readonly ConditionalWeakTable<IEntityType, AuditProperties> AuditPropertiesCache = new();

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
            if (entry.State is EntityState.Detached or EntityState.Unchanged)
            {
                continue;
            }

            UpdateEntry(entry, now);
        }
    }

    private static void UpdateEntry(EntityEntry entry, DateTime now)
    {
        var auditProperties = GetAuditProperties(entry.Metadata);

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

    internal static AuditProperties GetAuditProperties(IEntityType entityType)
    {
        return AuditPropertiesCache.GetValue(entityType, static metadata => new AuditProperties(
            FindAuditProperty(metadata, EfConstants.CreatedAt),
            FindAuditProperty(metadata, EfConstants.UpdatedAt),
            FindAuditProperty(metadata, EfConstants.DeletedAt)));
    }

    private static void UpdateAddedEntry(EntityEntry entry, AuditProperties auditProperties, DateTime now)
    {
        SetCurrentValue(entry, auditProperties.CreatedAt, now);
        SetCurrentValue(entry, auditProperties.UpdatedAt, now);
    }

    private static void UpdateModifiedEntry(EntityEntry entry, AuditProperties auditProperties, DateTime now)
    {
        SetCurrentValue(entry, auditProperties.UpdatedAt, now);
        SetNotModified(entry, auditProperties.CreatedAt);
    }

    private static void UpdateDeletedEntry(EntityEntry entry, AuditProperties auditProperties, DateTime now)
    {
        if (auditProperties.DeletedAt is null)
        {
            return;
        }

        entry.State = EntityState.Modified;

        SetCurrentValue(entry, auditProperties.DeletedAt, now);
        SetCurrentValue(entry, auditProperties.UpdatedAt, now);
        SetNotModified(entry, auditProperties.CreatedAt);
    }

    private static void SetCurrentValue(EntityEntry entry, IProperty? property, DateTime value)
    {
        if (property is null)
        {
            return;
        }

        var isDateTime = property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?);
        entry.Property(property.Name).CurrentValue = isDateTime
            ? value
            : property.GetTypeMapping().Converter?.ConvertFromProvider(value) ?? value;
    }

    private static void SetNotModified(EntityEntry entry, IProperty? property)
    {
        if (property is not null)
        {
            entry.Property(property.Name).IsModified = false;
        }
    }

    private static IProperty? FindAuditProperty(IEntityType entityType, string columnName)
    {
        if (entityType.FindProperty(columnName) is { } property)
        {
            return property;
        }

        var table = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table);

        return table.HasValue
            ? entityType.GetProperties().FirstOrDefault(item => item.GetColumnName(table.Value) == columnName)
            : null;
    }

    internal sealed record AuditProperties(
        IProperty? CreatedAt,
        IProperty? UpdatedAt,
        IProperty? DeletedAt);
}
