using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Предоставляет базовую конфигурацию аудируемой сущности со shadow-свойствами аудита и поддержкой soft-delete.
/// </summary>
public abstract class AuditableEntityConfiguration<TEntity> : IEntityTypeConfiguration<TEntity>
    where TEntity : class
{
    /// <summary>
    /// Применяет конфигурацию сущности, shadow-свойства аудита и фильтр soft-delete.
    /// </summary>
    /// <param name="builder">Построитель конфигурации сущности.</param>
    public virtual void Configure(EntityTypeBuilder<TEntity> builder)
    {
        ConfigureEntity(builder);
        ConfigureAudit(builder);
        ConfigureSoftDelete(builder);
    }

    /// <summary>
    /// Настраивает конфигурацию, специфичную для конкретной сущности.
    /// </summary>
    /// <param name="builder">Построитель конфигурации сущности.</param>
    protected abstract void ConfigureEntity(EntityTypeBuilder<TEntity> builder);

    /// <summary>
    /// Добавляет shadow-свойства, используемые для аудита сущности.
    /// </summary>
    /// <param name="builder">Построитель конфигурации сущности.</param>
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
    /// Применяет глобальный фильтр, скрывающий записи, помеченные как удаленные.
    /// </summary>
    /// <param name="builder">Построитель конфигурации сущности.</param>
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
    /// Определяет, должен ли для сущности использоваться soft-delete.
    /// </summary>
    /// <returns>
    /// <see langword="true"/>, если глобальный фильтр soft-delete должен быть включен; иначе <see langword="false"/>.
    /// </returns>
    protected virtual bool IsSoftDeleteEnabled()
    {
        return true;
    }
}
