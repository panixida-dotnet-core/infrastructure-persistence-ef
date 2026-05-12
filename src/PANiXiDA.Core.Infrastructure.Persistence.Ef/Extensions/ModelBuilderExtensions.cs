using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class ModelBuilderExtensions
{
    public static void ApplyPluralTableNames(this ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned())
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
