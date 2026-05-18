using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Provides base configuration for an auditable entity with audit shadow properties and soft-delete support.
/// </summary>
/// <typeparam name="TEntity">The entity type to configure.</typeparam>
public abstract class AuditableEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Applies entity-specific configuration, audit shadow properties, and the soft-delete query filter.
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
    /// Adds shadow properties used to audit the entity.
    /// </summary>
    /// <param name="builder">The entity type builder to configure.</param>
    protected virtual void ConfigureAudit(EntityTypeBuilder<TEntity> builder)
    {
        builder.Property<DateTime>(EfConstants.CreatedAt)
            .IsRequired()
            .HasColumnOrder(1);

        builder.Property<DateTime>(EfConstants.UpdatedAt)
            .IsRequired()
            .HasColumnOrder(2);

        builder.Property<DateTime?>(EfConstants.DeletedAt)
            .HasColumnOrder(3);
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

        builder.HasQueryFilter(item =>
            EF.Property<DateTime?>(item, EfConstants.DeletedAt) == null);
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
}
