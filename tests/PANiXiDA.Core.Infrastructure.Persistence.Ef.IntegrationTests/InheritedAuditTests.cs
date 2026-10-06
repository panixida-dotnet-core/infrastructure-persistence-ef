using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class InheritedAuditTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "Audit discovery resolves inherited converted properties from the base table in TPT")]
    public void GetAuditProperties_ResolvesBaseTableProperties()
    {
        using var context = CreateContext(PostgreSqlContainerFixture.CreateDatabaseName(), DateTimeOffset.UtcNow);
        var entityType = context.Model.FindEntityType(typeof(DerivedAuditEntity))!;
        var derivedTable = StoreObjectIdentifier.Create(entityType, StoreObjectType.Table)!.Value;
        var creation = entityType.GetProperty(nameof(InheritedAuditEntity.Creation));

        var properties = AuditSaveChangesInterceptor.GetAuditProperties(entityType);

        creation.GetColumnName(derivedTable).Should().BeNull();
        properties.CreatedAt.Should().BeSameAs(creation);
        properties.UpdatedAt.Should().BeSameAs(entityType.GetProperty(nameof(InheritedAuditEntity.Update)));
        properties.DeletedAt.Should().BeSameAs(entityType.GetProperty(nameof(InheritedAuditEntity.Deletion)));
        AuditSaveChangesInterceptor.GetAuditProperties(entityType).Should().BeSameAs(properties);
    }

    [Fact(DisplayName = "TPT derived entities update inherited audit timestamps and retain both rows after soft deletion")]
    public async Task SaveChanges_SupportsInheritedAuditLifecycle()
    {
        var databaseName = PostgreSqlContainerFixture.CreateDatabaseName();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var updatedAt = createdAt.AddDays(1);
        var deletedAt = updatedAt.AddDays(1);
        var entityId = Guid.NewGuid();

        await using (var context = CreateContext(databaseName, createdAt))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var entity = new DerivedAuditEntity { Id = entityId, Name = "Created" };
            context.Add(entity);

            context.SaveChanges();

            entity.Creation.Value.Should().Be(createdAt.UtcDateTime);
            entity.Update.Value.Should().Be(createdAt.UtcDateTime);
            entity.Deletion.Should().BeNull();
        }

        await using (var context = CreateContext(databaseName, updatedAt))
        {
            var entity = await context.Set<DerivedAuditEntity>().SingleAsync(TestContext.Current.CancellationToken);
            entity.Name = "Updated";
            entity.Creation = new AuditCreation(updatedAt.UtcDateTime);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            entity.Update.Value.Should().Be(updatedAt.UtcDateTime);
            context.Entry(entity).Property(item => item.Creation).IsModified.Should().BeFalse();
        }

        await using (var context = CreateContext(databaseName, deletedAt))
        {
            var entity = await context.Set<DerivedAuditEntity>().SingleAsync(TestContext.Current.CancellationToken);
            entity.Creation.Value.Should().Be(createdAt.UtcDateTime);
            entity.Update.Value.Should().Be(updatedAt.UtcDateTime);
            context.Remove(entity);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            entity.Update.Value.Should().Be(deletedAt.UtcDateTime);
            entity.Deletion!.Value.Value.Should().Be(deletedAt.UtcDateTime);
        }

        await using var assertContext = CreateContext(databaseName, deletedAt);
        var visible = await assertContext.Set<DerivedAuditEntity>().ToListAsync(TestContext.Current.CancellationToken);
        var persisted = await assertContext.Set<DerivedAuditEntity>().IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken);
        var baseRow = await assertContext.Set<InheritedAuditEntity>().IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken);

        visible.Should().BeEmpty();
        persisted.Id.Should().Be(entityId);
        persisted.Name.Should().Be("Updated");
        persisted.Creation.Value.Should().Be(createdAt.UtcDateTime);
        persisted.Update.Value.Should().Be(deletedAt.UtcDateTime);
        persisted.Deletion!.Value.Value.Should().Be(deletedAt.UtcDateTime);
        baseRow.Id.Should().Be(entityId);
    }

    private InheritedAuditDbContext CreateContext(string databaseName, DateTimeOffset now)
    {
        var options = new DbContextOptionsBuilder<InheritedAuditDbContext>(
            fixture.CreateOptions<InheritedAuditDbContext>(databaseName));
        options.AddInterceptors(new AuditSaveChangesInterceptor(new FixedTimeProvider(now)));
        return new InheritedAuditDbContext(options.Options);
    }
}

internal class InheritedAuditEntity
{
    public Guid Id { get; init; }

    public AuditCreation Creation { get; set; } = null!;

    public AuditUpdate Update { get; private set; }

    public AuditDeletion? Deletion { get; private set; }
}

internal sealed class DerivedAuditEntity : InheritedAuditEntity
{
    public string Name { get; set; } = string.Empty;
}

internal sealed class InheritedAuditDbContext(DbContextOptions<InheritedAuditDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new InheritedAuditConfiguration("audit_base"));
        modelBuilder.Entity<DerivedAuditEntity>().ToTable("audit_derived");
    }
}

internal sealed class InheritedAuditConfiguration(string tableName) : AuditableEntityConfiguration<InheritedAuditEntity>
{
    protected override void ConfigureEntity(EntityTypeBuilder<InheritedAuditEntity> builder)
    {
        builder.UseTptMappingStrategy().ToTable(tableName);
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Creation)
            .HasConversion(value => value.Value, value => new AuditCreation(value))
            .HasColumnName(EfConstants.CreatedAt);
        builder.Property(item => item.Update)
            .HasConversion(value => value.Value, value => new AuditUpdate(value))
            .HasColumnName(EfConstants.UpdatedAt);
        builder.Property(item => item.Deletion)
            .HasConversion(value => value!.Value.Value, value => new AuditDeletion(value))
            .HasColumnName(EfConstants.DeletedAt);
    }
}
