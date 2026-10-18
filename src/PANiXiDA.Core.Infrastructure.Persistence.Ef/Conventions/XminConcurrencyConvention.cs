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
            if (!typeof(IEntity).IsAssignableFrom(entityType.ClrType)
                || entityType.BaseType is not null
                || entityType.FindPrimaryKey() is null
                || entityType.GetTableName() is null
                || entityType.IsMappedToJson())
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
