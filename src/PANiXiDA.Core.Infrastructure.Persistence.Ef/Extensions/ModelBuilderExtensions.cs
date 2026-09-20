using Microsoft.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Metadata;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class ModelBuilderExtensions
{
    public static void ApplyPluralTableNames(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (ShouldSkipEntityType(entityType))
            {
                continue;
            }

            if (entityType.GetViewName() is not null)
            {
                continue;
            }

            var tableName = entityType.GetTableName();
            if (string.IsNullOrWhiteSpace(tableName))
            {
                continue;
            }

            entityType.SetTableName(tableName.ToPluralTableName());
        }
    }

    private static bool ShouldSkipEntityType(IMutableEntityType entityType)
    {
        var ownership = entityType.FindOwnership();
        if (ownership is null)
        {
            return false;
        }

        if (ownership.IsUnique)
        {
            return true;
        }

        var conventionEntityType = (IConventionEntityType)entityType;
        return conventionEntityType.GetTableNameConfigurationSource() == ConfigurationSource.Explicit;
    }
}
