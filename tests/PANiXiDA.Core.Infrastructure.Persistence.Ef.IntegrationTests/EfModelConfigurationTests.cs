using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfModelConfigurationTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "WriteDbContext applies audit properties, soft-delete filter, plural table names, and HiLo")]
    public async Task WriteDbContext_AppliesAuditPropertiesSoftDeleteFilterPluralTableNamesAndHiLo()
    {
        await using var context = await CreateWriteContextAsync<TestWriteDbContext>(
            options => new TestWriteDbContext(options, []));

        var entityType = GetDesignTimeEntityType(context, typeof(TestAggregateRoot));

        entityType.GetTableName().Should().Be("aggregates");
        entityType.GetDeclaredQueryFilters().Should().NotBeEmpty();
        entityType.GetProperty(EfConstants.CreatedAt).IsNullable.Should().BeFalse();
        entityType.GetProperty(EfConstants.UpdatedAt).IsNullable.Should().BeFalse();
        entityType.GetProperty(EfConstants.DeletedAt).IsNullable.Should().BeTrue();
        entityType.GetProperty(EfConstants.CreatedAt).GetColumnOrder().Should().Be(1);
        entityType.GetProperty(EfConstants.UpdatedAt).GetColumnOrder().Should().Be(2);
        entityType.GetProperty(EfConstants.DeletedAt).GetColumnOrder().Should().Be(3);
        context.GetService<IDesignTimeModel>()
            .Model
            .GetSequences()
            .Should()
            .Contain(sequence => sequence.Name == "EntityFrameworkHiLoSequence");
    }

    [Fact(DisplayName = "WriteDbContext can use context name as schema")]
    public async Task WriteDbContext_CanUseContextNameAsSchema()
    {
        await using var context = await CreateWriteContextAsync<SchemaWriteDbContext>(
            options => new SchemaWriteDbContext(options, []));

        var entityType = GetDesignTimeEntityType(context, typeof(TestAggregateRoot));

        entityType.GetSchema().Should().Be("schema");
    }

    [Fact(DisplayName = "WriteDbContext can keep singular table names")]
    public async Task WriteDbContext_CanKeepSingularTableNames()
    {
        await using var context = await CreateWriteContextAsync<SingularWriteDbContext>(
            options => new SingularWriteDbContext(options, []));

        var entityType = GetDesignTimeEntityType(context, typeof(TestAggregateRoot));

        entityType.GetTableName().Should().Be(nameof(TestAggregateRoot));
    }

    [Fact(DisplayName = "AuditableEntityConfiguration can disable soft-delete query filter")]
    public async Task AuditableEntityConfiguration_CanDisableSoftDeleteQueryFilter()
    {
        await using var context = await CreateWriteContextAsync<TestWriteDbContext>(
            options => new TestWriteDbContext(options, []));

        var entityType = GetDesignTimeEntityType(context, typeof(NoSoftDeleteAggregateRoot));

        entityType.GetDeclaredQueryFilters().Should().BeEmpty();
    }

    [Fact(DisplayName = "ReadDbContext registers read models as no-tracking models excluded from migrations")]
    public async Task ReadDbContext_RegistersReadModelsAsNoTrackingModelsExcludedFromMigrations()
    {
        await using var context = await CreateReadContextAsync<TestReadDbContext>(
            options => new TestReadDbContext(options));

        var entityType = GetDesignTimeEntityType(context, typeof(ProductReadDbModel));

        context.ChangeTracker.QueryTrackingBehavior.Should().Be(QueryTrackingBehavior.NoTracking);
        entityType.GetTableName().Should().Be("products");
        entityType.GetSchema().Should().BeNull();
        entityType.IsTableExcludedFromMigrations().Should().BeTrue();
    }

    [Fact(DisplayName = "ReadDbContext can use context name as schema")]
    public async Task ReadDbContext_CanUseContextNameAsSchema()
    {
        await using var context = await CreateReadContextAsync<SchemaReadDbContext>(
            options => new SchemaReadDbContext(options));

        var entityType = GetDesignTimeEntityType(context, typeof(ProductReadDbModel));

        entityType.GetSchema().Should().Be("schema");
        entityType.IsTableExcludedFromMigrations().Should().BeTrue();
    }

    [Fact(DisplayName = "ReadDbContext can include read models in migrations")]
    public async Task ReadDbContext_CanIncludeReadModelsInMigrations()
    {
        await using var context = await CreateReadContextAsync<IncludedReadDbContext>(
            options => new IncludedReadDbContext(options));

        var entityType = GetDesignTimeEntityType(context, typeof(ProductReadDbModel));

        entityType.GetSchema().Should().BeNull();
        entityType.IsTableExcludedFromMigrations().Should().BeFalse();
    }

    [Fact(DisplayName = "ReadDbContext can include read models in migrations with schema")]
    public async Task ReadDbContext_CanIncludeReadModelsInMigrationsWithSchema()
    {
        await using var context = await CreateReadContextAsync<SchemaIncludedReadDbContext>(
            options => new SchemaIncludedReadDbContext(options));

        var entityType = GetDesignTimeEntityType(context, typeof(ProductReadDbModel));

        entityType.GetSchema().Should().Be("schema_included");
        entityType.IsTableExcludedFromMigrations().Should().BeFalse();
    }

    [Fact(DisplayName = "ReadDbContext applies soft-delete filter to auditable read models")]
    public async Task ReadDbContext_AppliesSoftDeleteFilterToAuditableReadModels()
    {
        await using var context = await CreateReadContextAsync<IncludedReadDbContext>(
            options => new IncludedReadDbContext(options));

        context.Set<ProductAuditableReadDbModel>().AddRange(
            new ProductAuditableReadDbModel
            {
                Id = 1,
                Name = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new ProductAuditableReadDbModel
            {
                Id = 2,
                Name = "Deleted",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                DeletedAt = DateTime.UtcNow
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var items = await context.Set<ProductAuditableReadDbModel>()
            .ToListAsync(TestContext.Current.CancellationToken);

        items.Should().ContainSingle();
        items[0].Name.Should().Be("Active");
    }

    [Fact(DisplayName = "ModelBuilderExtensions applies plural table names and preserves explicitly mapped owned tables")]
    public void ModelBuilderExtensions_AppliesPluralTableNamesAndPreservesExplicitlyMappedOwnedTables()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<ModelBuilderOwner>(builder =>
        {
            builder.HasKey(item => item.Id);
            builder.OwnsOne(item => item.Owned, ownedBuilder =>
            {
                ownedBuilder.ToTable("OwnedThing");
            });
            builder.OwnsMany(item => item.OwnedItems, ownedBuilder =>
            {
                ownedBuilder.HasKey(item => item.Id);
            });
            builder.OwnsMany(item => item.LegacyOwnedItems, ownedBuilder =>
            {
                ownedBuilder.ToTable("LegacyOwnedItem");
                ownedBuilder.HasKey(item => item.Id);
            });
        });
        modelBuilder.Entity<ModelBuilderView>().ToView("model_builder_view");
        modelBuilder.Entity<ModelBuilderNameless>().ToTable((string?)null);

        modelBuilder.ApplyPluralTableNames();

        var ownerType = modelBuilder.Model.FindEntityType(typeof(ModelBuilderOwner))!;
        var ownedType = modelBuilder.Model.GetEntityTypes().Single(item =>
            item.ClrType == typeof(ModelBuilderOwned));
        var ownedItemType = modelBuilder.Model.GetEntityTypes().Single(item =>
            item.ClrType == typeof(ModelBuilderOwnedItem));
        var legacyOwnedItemType = modelBuilder.Model.GetEntityTypes().Single(item =>
            item.ClrType == typeof(ModelBuilderLegacyOwnedItem));
        var viewType = modelBuilder.Model.FindEntityType(typeof(ModelBuilderView))!;
        var namelessType = modelBuilder.Model.FindEntityType(typeof(ModelBuilderNameless))!;

        ownerType.GetTableName().Should().Be("model_builder_owners");
        ownedType.GetTableName().Should().Be("OwnedThing");
        ownedItemType.GetTableName().Should().Be("model_builder_owned_items");
        legacyOwnedItemType.GetTableName().Should().Be("LegacyOwnedItem");
        viewType.GetViewName().Should().Be("model_builder_view");
        namelessType.GetTableName().Should().BeNull();
    }

    [Fact(DisplayName = "ShouldSkipOwnedEntityType treats missing ownership metadata as skipped")]
    public void ShouldSkipOwnedEntityType_TreatsMissingOwnershipMetadataAsSkipped()
    {
        var modelBuilder = new ModelBuilder();
        var entityType = modelBuilder.Entity<ModelBuilderOwner>().Metadata;

        var shouldSkip = InvokeShouldSkipOwnedEntityType(entityType);

        shouldSkip.Should().BeTrue();
    }

    [Fact(DisplayName = "ShouldSkipOwnedEntityType handles mutable metadata without convention metadata")]
    public void ShouldSkipOwnedEntityType_HandlesMutableMetadataWithoutConventionMetadata()
    {
        var modelBuilder = new ModelBuilder();
        modelBuilder.Entity<ModelBuilderOwner>(builder =>
        {
            builder.HasKey(item => item.Id);
            builder.OwnsMany(item => item.OwnedItems, ownedBuilder =>
            {
                ownedBuilder.HasKey(item => item.Id);
            });
        });
        var ownedItemType = modelBuilder.Model.GetEntityTypes().Single(item =>
            item.ClrType == typeof(ModelBuilderOwnedItem));
        var ownership = ownedItemType.FindOwnership();
        if (ownership is null)
        {
            throw new InvalidOperationException("The test model must contain ownership metadata.");
        }

        var entityType = DispatchProxy.Create<IMutableEntityType, MutableEntityTypeProxy>();
        var proxy = (MutableEntityTypeProxy)(object)entityType;
        proxy.Ownership = ownership;

        var shouldSkip = InvokeShouldSkipOwnedEntityType(entityType);

        shouldSkip.Should().BeFalse();
    }

    [Fact(DisplayName = "ModelBuilderExtensions handles assemblies without read models")]
    public void ModelBuilderExtensions_HandlesAssembliesWithoutReadModels()
    {
        var modelBuilder = new ModelBuilder();

        modelBuilder.RegisterReadDbModels(typeof(string).Assembly, null, true);

        modelBuilder.Model.GetEntityTypes().Should().BeEmpty();
    }

    [Fact(DisplayName = "TrimFirstMatchingSuffix returns original value when no suffix matches")]
    public void TrimFirstMatchingSuffix_ReturnsOriginalValue_WhenNoSuffixMatches()
    {
        var result = "Order".TrimFirstMatchingSuffix("ReadDbModel");

        result.Should().Be("Order");
    }

    private Task<TDbContext> CreateWriteContextAsync<TDbContext>(
        Func<DbContextOptions<TDbContext>, TDbContext> factory)
        where TDbContext : DbContext
    {
        return fixture.CreateInitializedDbContextAsync(factory);
    }

    private Task<TDbContext> CreateReadContextAsync<TDbContext>(
        Func<DbContextOptions<TDbContext>, TDbContext> factory)
        where TDbContext : DbContext
    {
        return fixture.CreateInitializedDbContextAsync(factory);
    }

    private static IReadOnlyEntityType GetDesignTimeEntityType(DbContext context, Type clrType)
    {
        return context.GetService<IDesignTimeModel>().Model.FindEntityType(clrType)!;
    }

    private static bool InvokeShouldSkipOwnedEntityType(IMutableEntityType entityType)
    {
        var method = typeof(ModelBuilderExtensions).GetMethod(
            "ShouldSkipOwnedEntityType",
            BindingFlags.Static | BindingFlags.NonPublic);
        if (method is null)
        {
            throw new MissingMethodException(
                typeof(ModelBuilderExtensions).FullName,
                "ShouldSkipOwnedEntityType");
        }

        var result = method.Invoke(null, [entityType]);
        return result is bool shouldSkip
            ? shouldSkip
            : throw new InvalidOperationException("ShouldSkipOwnedEntityType must return a Boolean value.");
    }

    private class MutableEntityTypeProxy : DispatchProxy
    {
        public IMutableForeignKey? Ownership { get; set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);

            if (targetMethod.Name == nameof(IMutableEntityType.FindOwnership))
            {
                return Ownership;
            }

            return targetMethod.ReturnType.IsValueType
                ? Activator.CreateInstance(targetMethod.ReturnType)
                : null;
        }
    }

}
