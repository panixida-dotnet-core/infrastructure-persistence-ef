using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ConvertedAuditTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "Audit configuration reuses converted CLR properties without creating duplicate shadow properties")]
    public void Configure_ReusesConvertedProperties()
    {
        using var context = CreateContext(PostgreSqlContainerFixture.CreateDatabaseName(), DateTimeOffset.UtcNow, false);

        var entityType = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ConvertedAuditEntity))!;
        var creation = entityType.GetProperty(nameof(ConvertedAuditEntity.Creation));
        var update = entityType.GetProperty(nameof(ConvertedAuditEntity.Update));
        var deletion = entityType.GetProperty(nameof(ConvertedAuditEntity.Deletion));

        entityType.FindProperty(EfConstants.CreatedAt).Should().BeNull();
        entityType.FindProperty(EfConstants.UpdatedAt).Should().BeNull();
        entityType.FindProperty(EfConstants.DeletedAt).Should().BeNull();
        creation.GetColumnName().Should().Be(EfConstants.CreatedAt);
        update.GetColumnName().Should().Be(EfConstants.UpdatedAt);
        deletion.GetColumnName().Should().Be(EfConstants.DeletedAt);
        creation.IsShadowProperty().Should().BeFalse();
        update.IsShadowProperty().Should().BeFalse();
        deletion.IsShadowProperty().Should().BeFalse();
        creation.IsNullable.Should().BeFalse();
        update.IsNullable.Should().BeFalse();
        deletion.IsNullable.Should().BeTrue();
        creation.GetColumnOrder().Should().Be(1);
        update.GetColumnOrder().Should().Be(2);
        deletion.GetColumnOrder().Should().Be(3);
        creation.GetValueConverter().Should().NotBeNull();
        update.GetValueConverter().Should().NotBeNull();
        deletion.GetValueConverter().Should().NotBeNull();
    }

    [Theory(DisplayName = "Converted audit properties support creation, modification and soft deletion across contexts")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChanges_SupportsConvertedAuditLifecycle(bool snakeCase)
    {
        var databaseName = PostgreSqlContainerFixture.CreateDatabaseName();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var updatedAt = createdAt.AddDays(1);
        var deletedAt = updatedAt.AddDays(1);
        var entityId = Guid.NewGuid();

        await using (var context = CreateContext(databaseName, createdAt, snakeCase))
        {
            await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            var entity = new ConvertedAuditEntity { Id = entityId, Name = "Created" };
            context.ConvertedAuditEntities.Add(entity);

            context.SaveChanges();

            entity.Creation.Value.Should().Be(createdAt.UtcDateTime);
            entity.Update.Value.Should().Be(createdAt.UtcDateTime);
            entity.Deletion.Should().BeNull();
        }

        await using (var context = CreateContext(databaseName, updatedAt, snakeCase))
        {
            var entity = await context.ConvertedAuditEntities.SingleAsync(TestContext.Current.CancellationToken);
            entity.Name = "Updated";
            entity.Creation = new AuditCreation(updatedAt.UtcDateTime);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            entity.Update.Value.Should().Be(updatedAt.UtcDateTime);
            context.Entry(entity).Property(item => item.Creation).IsModified.Should().BeFalse();
        }

        await using (var context = CreateContext(databaseName, deletedAt, snakeCase))
        {
            var entity = await context.ConvertedAuditEntities.SingleAsync(TestContext.Current.CancellationToken);
            entity.Creation.Value.Should().Be(createdAt.UtcDateTime);
            entity.Update.Value.Should().Be(updatedAt.UtcDateTime);
            context.ConvertedAuditEntities.Remove(entity);

            await context.SaveChangesAsync(TestContext.Current.CancellationToken);

            entity.Creation.Value.Should().Be(createdAt.UtcDateTime);
            entity.Update.Value.Should().Be(deletedAt.UtcDateTime);
            entity.Deletion!.Value.Value.Should().Be(deletedAt.UtcDateTime);
        }

        await using var assertContext = CreateContext(databaseName, deletedAt, snakeCase);
        var visible = await assertContext.ConvertedAuditEntities.ToListAsync(TestContext.Current.CancellationToken);
        var persisted = await assertContext.ConvertedAuditEntities.IgnoreQueryFilters()
            .SingleAsync(TestContext.Current.CancellationToken);

        visible.Should().BeEmpty();
        persisted.Id.Should().Be(entityId);
        persisted.Creation.Value.Should().Be(createdAt.UtcDateTime);
        persisted.Update.Value.Should().Be(deletedAt.UtcDateTime);
        persisted.Deletion!.Value.Value.Should().Be(deletedAt.UtcDateTime);
    }

    [Fact(DisplayName = "Added converted audit properties retain the existing timestamp overwrite behavior")]
    public async Task SaveChanges_OverwritesExplicitCreationTimestamp()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using var context = CreateContext(PostgreSqlContainerFixture.CreateDatabaseName(), now, false);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var entity = new ConvertedAuditEntity
        {
            Id = Guid.NewGuid(),
            Creation = new AuditCreation(now.AddDays(-1).UtcDateTime)
        };
        context.ConvertedAuditEntities.Add(entity);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        entity.Creation.Value.Should().Be(now.UtcDateTime);
        entity.Update.Value.Should().Be(now.UtcDateTime);
    }

    private TestWriteDbContext CreateContext(string databaseName, DateTimeOffset now, bool snakeCase)
    {
        var options = new DbContextOptionsBuilder<TestWriteDbContext>(fixture.CreateOptions<TestWriteDbContext>(databaseName));
        if (snakeCase)
        {
            options.UseSnakeCaseNamingConvention();
        }

        return new TestWriteDbContext(options.Options, [new AuditSaveChangesInterceptor(new FixedTimeProvider(now))]);
    }
}
