using Microsoft.EntityFrameworkCore;

using System.Reflection;

using Microsoft.EntityFrameworkCore.Metadata;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class ModelBuilderExtensions
{
    public static void ApplyPluralTableNames(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned() && ShouldSkipOwnedEntityType(entityType))
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

    private static bool ShouldSkipOwnedEntityType(IMutableEntityType entityType)
    {
        var ownership = entityType.FindOwnership();
        if (ownership?.IsUnique is not false)
        {
            return true;
        }

        return entityType is IConventionEntityType conventionEntityType
            && conventionEntityType.GetTableNameConfigurationSource() == ConfigurationSource.Explicit;
    }

    public static void RegisterReadDbModels(
        this ModelBuilder modelBuilder,
        Assembly assembly,
        string? schemaName,
        bool excludeFromMigrations)
    {
        var readDbModelTypes = assembly
            .GetTypes()
            .Where(type =>
            {
                return type is { IsClass: true, IsAbstract: false }
                    && type.HasGenericBaseType(typeof(ReadDbModel<>));
            });

        foreach (var readDbModelType in readDbModelTypes)
        {
            modelBuilder.ConfigureReadDbModel(
                readDbModelType,
                schemaName,
                excludeFromMigrations);
        }
    }

    private static void ConfigureReadDbModel(
        this ModelBuilder modelBuilder,
        Type readDbModelType,
        string? schemaName,
        bool excludeFromMigrations)
    {
        var entityBuilder = modelBuilder.Entity(readDbModelType);

        var tableName = readDbModelType.ToTableName(
            nameof(ReadDbModel<>));

        if (excludeFromMigrations)
        {
            if (schemaName is null)
            {
                entityBuilder.ToTable(tableName, tableBuilder =>
                {
                    tableBuilder.ExcludeFromMigrations();
                });

                return;
            }

            entityBuilder.ToTable(tableName, schemaName, tableBuilder =>
            {
                tableBuilder.ExcludeFromMigrations();
            });

            return;
        }

        if (schemaName is null)
        {
            entityBuilder.ToTable(tableName);
            return;
        }

        entityBuilder.ToTable(tableName, schemaName);
    }
}
