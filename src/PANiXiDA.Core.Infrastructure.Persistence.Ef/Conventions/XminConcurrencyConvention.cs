using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

using PANiXiDA.Core.Domain.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Conventions;

internal sealed class XminConcurrencyConvention : IModelFinalizingConvention
{
    public void ProcessModelFinalizing(
        IConventionModelBuilder modelBuilder,
        IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes())
        {
            if (!entityType.GetDerivedTypesInclusive().Any(type => typeof(IEntity).IsAssignableFrom(type.ClrType))
                || entityType.FindPrimaryKey() is null
                || (entityType.GetTableName() is null && !entityType.GetDerivedTypes().Any(derived => derived.GetTableName() is not null))
                || entityType.IsMappedToJson())
            {
                continue;
            }

            if (entityType.GetMappingStrategy() == RelationalAnnotationNames.TptMappingStrategy)
            {
                throw new InvalidOperationException($"Automatic xmin concurrency does not support TPT mapping for domain entity '{entityType.Name}'. Use TPH or TPC mapping instead.");
            }

            if (entityType.GetMappingFragments(StoreObjectType.Table).Any())
            {
                throw new InvalidOperationException($"Automatic xmin concurrency does not support entity splitting for domain entity '{entityType.Name}'. Map the entity to a single table instead.");
            }

            if (entityType.BaseType is not null)
            {
                continue;
            }

            if (entityType.GetProperties().Any(property =>
                property.ClrType == typeof(uint)
                && property.IsConcurrencyToken
                && property.ValueGenerated == ValueGenerated.OnAddOrUpdate))
            {
                continue;
            }

            var propertyBuilder = entityType.Builder.Property(typeof(uint), "xmin")!;
            propertyBuilder.HasColumnName("xmin");
            propertyBuilder.IsConcurrencyToken(true);
            propertyBuilder.ValueGenerated(ValueGenerated.OnAddOrUpdate);
        }
    }
}
