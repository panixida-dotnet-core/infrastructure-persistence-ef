using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Provides base configuration for an auditable entity with audit properties and soft-delete support.
/// </summary>
/// <typeparam name="TEntity">The entity type to configure.</typeparam>
public abstract class AuditableEntityConfiguration<
    [DynamicallyAccessedMembers(TrimmingConstants.EntityMembers)] TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Applies entity-specific configuration, audit properties, and the soft-delete query filter.
    /// </summary>
    /// <param name="builder">The entity type builder to configure.</param>
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ConfigureEntity(builder);
        ConfigureAudit(builder);
        ConfigureSoftDelete(builder);
    }

    /// <summary>
    /// Configures entity-specific mapping.
    /// </summary>
    /// <param name="builder">The entity type builder to configure.</param>
    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);

    /// <summary>
    /// Reuses properties mapped to audit columns, adding shadow properties when no matching property exists.
    /// </summary>
    /// <param name="builder">The entity type builder to configure.</param>
    protected virtual void ConfigureAudit(EntityTypeBuilder<TEntity> builder)
    {
        ConfigureAuditProperty(builder, EfConstants.CreatedAt, true, 1);
        ConfigureAuditProperty(builder, EfConstants.UpdatedAt, true, 2);
        ConfigureAuditProperty(builder, EfConstants.DeletedAt, false, 3);
    }

    /// <summary>
    /// Applies a global query filter that hides rows marked as deleted.
    /// </summary>
    /// <param name="builder">The entity type builder to configure.</param>
    protected virtual void ConfigureSoftDelete(EntityTypeBuilder<TEntity> builder)
    {
        if (!IsSoftDeleteEnabled())
        {
            return;
        }

        var property = FindAuditProperty(builder.Metadata, EfConstants.DeletedAt);
        var propertyName = property is null ? EfConstants.DeletedAt : property.Name;

        builder.HasQueryFilter(item => EF.Property<object?>(item, propertyName) == null);
    }

    /// <summary>
    /// Determines whether the soft-delete query filter should be enabled for the entity.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the soft-delete query filter should be enabled; otherwise, <see langword="false"/>.
    /// </returns>
    protected virtual bool IsSoftDeleteEnabled()
    {
        return true;
    }

    private static void ConfigureAuditProperty(
        EntityTypeBuilder<TEntity> builder,
        string columnName,
        bool isRequired,
        int columnOrder)
    {
        var property = FindAuditProperty(builder.Metadata, columnName)
            ?? (isRequired
                ? builder.Property<DateTime>(columnName).Metadata
                : builder.Property<DateTime?>(columnName).Metadata);

        property.IsNullable = !isRequired;
        property.SetColumnOrder(columnOrder);
    }

    private static IMutableProperty? FindAuditProperty(IMutableEntityType entityType, string columnName)
    {
        return entityType.FindProperty(columnName)
            ?? entityType.GetProperties().FirstOrDefault(property => property.GetColumnName() == columnName);
    }
}
