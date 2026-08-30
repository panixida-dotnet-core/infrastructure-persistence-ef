using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.TestDoubles;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AuditSaveChangesInterceptorTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "SavingChangesAsync sets audit values for added entities")]
    public async Task SavingChangesAsync_SetsAuditValuesForAddedEntities()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using var context = await CreateContextAsync(now);
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New())
        {
            Name = "Created"
        };

        context.Aggregates.Add(aggregateRoot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = context.Entry(aggregateRoot);
        entry.Property<DateTime>(EfConstants.CreatedAt).CurrentValue.Should().Be(now.UtcDateTime);
        entry.Property<DateTime>(EfConstants.UpdatedAt).CurrentValue.Should().Be(now.UtcDateTime);
        entry.Property<DateTime?>(EfConstants.DeletedAt).CurrentValue.Should().BeNull();
    }

    [Fact(DisplayName = "SavingChanges sets audit values when invoked directly")]
    public async Task SavingChanges_SetsAuditValues_WhenInvokedDirectly()
    {
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        await using var context = await fixture.CreateInitializedDbContextAsync<TestWriteDbContext>(
            options => new TestWriteDbContext(options, []));
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New())
        {
            Name = "Created"
        };
        var interceptor = new AuditSaveChangesInterceptor(new FixedTimeProvider(now));

        context.Aggregates.Add(aggregateRoot);
        interceptor.SavingChanges(CreateEventData(context), default);

        var entry = context.Entry(aggregateRoot);
        entry.Property<DateTime>(EfConstants.CreatedAt).CurrentValue.Should().Be(now.UtcDateTime);
        entry.Property<DateTime>(EfConstants.UpdatedAt).CurrentValue.Should().Be(now.UtcDateTime);
        entry.Property<DateTime?>(EfConstants.DeletedAt).CurrentValue.Should().BeNull();
    }

    [Fact(DisplayName = "SavingChangesAsync updates modified entities without changing CreatedAt")]
    public async Task SavingChangesAsync_UpdatesModifiedEntitiesWithoutChangingCreatedAt()
    {
        var databaseName = PostgreSqlContainerFixture.CreateDatabaseName();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero);

        await using (var createContext = await CreateContextAsync(createdAt, databaseName))
        {
            createContext.Aggregates.Add(new TestAggregateRoot(TestAggregateRootId.New())
            {
                Name = "Created"
            });

            await createContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var updateContext = await CreateExistingContextAsync(updatedAt, databaseName);
        var aggregateRoot = await updateContext.Aggregates
            .SingleAsync(TestContext.Current.CancellationToken);

        aggregateRoot.Name = "Updated";
        await updateContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = updateContext.Entry(aggregateRoot);
        entry.Property<DateTime>(EfConstants.CreatedAt).CurrentValue.Should().Be(createdAt.UtcDateTime);
        entry.Property<DateTime>(EfConstants.UpdatedAt).CurrentValue.Should().Be(updatedAt.UtcDateTime);
    }

    [Fact(DisplayName = "SavingChangesAsync updates modified entities without CreatedAt property")]
    public async Task SavingChangesAsync_UpdatesModifiedEntitiesWithoutCreatedAtProperty()
    {
        var databaseName = PostgreSqlContainerFixture.CreateDatabaseName();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var updatedAt = new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero);

        await using (var createContext = await CreateContextAsync(createdAt, databaseName))
        {
            createContext.UpdatedOnlyEntities.Add(new UpdatedOnlyEntity
            {
                Id = 1,
                Name = "Created"
            });

            await createContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var updateContext = await CreateExistingContextAsync(updatedAt, databaseName);
        var entity = await updateContext.UpdatedOnlyEntities
            .SingleAsync(TestContext.Current.CancellationToken);

        entity.Name = "Updated";
        await updateContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        updateContext.Entry(entity)
            .Property<DateTime>(EfConstants.UpdatedAt)
            .CurrentValue
            .Should()
            .Be(updatedAt.UtcDateTime);
    }

    [Fact(DisplayName = "SavingChangesAsync ignores unchanged entities")]
    public async Task SavingChangesAsync_IgnoresUnchangedEntities()
    {
        var databaseName = PostgreSqlContainerFixture.CreateDatabaseName();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var unchangedAt = new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero);

        await using (var createContext = await CreateContextAsync(createdAt, databaseName))
        {
            createContext.Aggregates.Add(new TestAggregateRoot(TestAggregateRootId.New())
            {
                Name = "Created"
            });

            await createContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var unchangedContext = await CreateExistingContextAsync(unchangedAt, databaseName);
        var aggregateRoot = await unchangedContext.Aggregates
            .SingleAsync(TestContext.Current.CancellationToken);

        await unchangedContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = unchangedContext.Entry(aggregateRoot);
        entry.Property<DateTime>(EfConstants.CreatedAt).CurrentValue.Should().Be(createdAt.UtcDateTime);
        entry.Property<DateTime>(EfConstants.UpdatedAt).CurrentValue.Should().Be(createdAt.UtcDateTime);
    }

    [Fact(DisplayName = "SavingChangesAsync converts deleted entities with DeletedAt to soft delete")]
    public async Task SavingChangesAsync_ConvertsDeletedEntitiesWithDeletedAtToSoftDelete()
    {
        var databaseName = PostgreSqlContainerFixture.CreateDatabaseName();
        var createdAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var deletedAt = new DateTimeOffset(2026, 1, 3, 3, 4, 5, TimeSpan.Zero);

        await using (var createContext = await CreateContextAsync(createdAt, databaseName))
        {
            createContext.Aggregates.Add(new TestAggregateRoot(TestAggregateRootId.New())
            {
                Name = "Created"
            });

            await createContext.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var deleteContext = await CreateExistingContextAsync(deletedAt, databaseName);
        var aggregateRoot = await deleteContext.Aggregates
            .SingleAsync(TestContext.Current.CancellationToken);

        deleteContext.Aggregates.Remove(aggregateRoot);
        await deleteContext.SaveChangesAsync(TestContext.Current.CancellationToken);

        var entry = deleteContext.Entry(aggregateRoot);
        entry.State.Should().Be(EntityState.Unchanged);
        entry.Property<DateTime?>(EfConstants.DeletedAt).CurrentValue.Should().Be(deletedAt.UtcDateTime);
        entry.Property<DateTime>(EfConstants.UpdatedAt).CurrentValue.Should().Be(deletedAt.UtcDateTime);
        var anyVisible = await deleteContext.Aggregates
            .AnyAsync(TestContext.Current.CancellationToken);
        var anyIncludingDeleted = await deleteContext.Aggregates
            .IgnoreQueryFilters()
            .AnyAsync(TestContext.Current.CancellationToken);

        anyVisible.Should().BeFalse();
        anyIncludingDeleted.Should().BeTrue();
    }

    [Fact(DisplayName = "SavingChanges preserves hard delete for entities without DeletedAt")]
    public async Task SavingChanges_PreservesHardDeleteForEntitiesWithoutDeletedAt()
    {
        await using var context = await CreateContextAsync(DateTimeOffset.UtcNow);
        var entity = new NonAuditableEntity
        {
            Id = 1
        };

        context.NonAuditableEntities.Add(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        context.NonAuditableEntities.Remove(entity);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var exists = await context.NonAuditableEntities.AnyAsync(TestContext.Current.CancellationToken);

        exists.Should().BeFalse();
    }

    [Fact(DisplayName = "AuditSaveChangesInterceptor ignores null DbContext event data")]
    public void AuditSaveChangesInterceptor_IgnoresNullDbContextEventData()
    {
        var interceptor = new AuditSaveChangesInterceptor(TimeProvider.System);

        var act = () => interceptor.SavingChanges(CreateEventData(context: null), default);

        act.Should().NotThrow();
    }

    private async Task<TestWriteDbContext> CreateContextAsync(
        DateTimeOffset utcNow,
        string? databaseName = null)
    {
        var targetDatabaseName = databaseName ?? PostgreSqlContainerFixture.CreateDatabaseName();

        return await fixture.CreateInitializedDbContextAsync<TestWriteDbContext>(
            targetDatabaseName,
            options => new TestWriteDbContext(
                options,
                [new AuditSaveChangesInterceptor(new FixedTimeProvider(utcNow))]));
    }

    private Task<TestWriteDbContext> CreateExistingContextAsync(DateTimeOffset utcNow, string databaseName)
    {
        var context = fixture.CreateDbContext<TestWriteDbContext>(
            databaseName,
            options => new TestWriteDbContext(
                options,
                [new AuditSaveChangesInterceptor(new FixedTimeProvider(utcNow))]));

        return Task.FromResult(context);
    }

    private static DbContextEventData CreateEventData(DbContext? context)
    {
        return new DbContextEventData(
            null!,
            static (_, _) => string.Empty,
            context);
    }
}
