using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "EfRepository AddAsync persists aggregate and tracks it")]
    public async Task AddAsync_PersistsAggregateAndTracksIt()
    {
        await using var context = await CreateContextAsync();
        var tracker = new AggregateTracker([context]);
        var repository = new ExposedWriteRepository(context);
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New())
        {
            Name = "Created"
        };

        await repository.AddAsync(aggregateRoot, TestContext.Current.CancellationToken);

        context.Entry(aggregateRoot).State.Should().Be(EntityState.Unchanged);
        var storedAggregateRoot = await context.Aggregates
            .AsNoTracking()
            .SingleAsync(item => item.Id == aggregateRoot.Id, TestContext.Current.CancellationToken);
        storedAggregateRoot.Name.Should().Be("Created");
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "EfRepository GetByIdAsync tracks aggregate even when query tracking is disabled by default")]
    public async Task GetByIdAsync_TracksAggregate_WhenQueryTrackingIsDisabledByDefault()
    {
        await using var context = await CreateContextAsync();
        var id = TestAggregateRootId.New();
        context.Aggregates.Add(new TestAggregateRoot(id)
        {
            Name = "Stored"
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;

        var repository = new ExposedWriteRepository(context);

        var aggregateRoot = await repository.GetByIdAsync(id, TestContext.Current.CancellationToken);

        aggregateRoot.Should().NotBeNull();
        aggregateRoot!.Name.Should().Be("Stored");
        context.Entry(aggregateRoot).State.Should().Be(EntityState.Unchanged);
        (await repository.GetByIdAsync(id, TestContext.Current.CancellationToken)).Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "EfRepository GetByIdAsync returns null when aggregate is not found")]
    public async Task GetByIdAsync_ReturnsNull_WhenAggregateIsNotFound()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedWriteRepository(context);

        var aggregateRoot = await repository.GetByIdAsync(
            TestAggregateRootId.New(),
            TestContext.Current.CancellationToken);

        aggregateRoot.Should().BeNull();
    }

    [Fact(DisplayName = "EfRepository UpdateAsync persists aggregate changes and tracks it")]
    public async Task UpdateAsync_PersistsAggregateChangesAndTracksIt()
    {
        await using var context = await CreateContextAsync();
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New())
        {
            Name = "Stored"
        };
        context.Aggregates.Add(aggregateRoot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var tracker = new AggregateTracker([context]);
        var repository = new ExposedWriteRepository(context);
        aggregateRoot = (await repository.GetByIdAsync(aggregateRoot.Id, TestContext.Current.CancellationToken))!;
        aggregateRoot.Name = "Updated";

        await repository.UpdateAsync(aggregateRoot, TestContext.Current.CancellationToken);

        context.Entry(aggregateRoot).State.Should().Be(EntityState.Unchanged);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
        context.ChangeTracker.Clear();
        var storedAggregateRoot = await context.Aggregates
            .AsNoTracking()
            .SingleAsync(item => item.Id == aggregateRoot.Id, TestContext.Current.CancellationToken);
        storedAggregateRoot.Name.Should().Be("Updated");
        tracker.GetAll().Should().BeEmpty();
    }

    [Fact(DisplayName = "EfRepository DeleteAsync persists aggregate deletion and retains it for event collection")]
    public async Task DeleteAsync_PersistsAggregateDeletionAndRetainsItForEventCollection()
    {
        await using var context = await CreateContextAsync();
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New())
        {
            Name = "Stored"
        };
        context.Aggregates.Add(aggregateRoot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracker = new AggregateTracker([context]);
        var repository = new ExposedWriteRepository(context);

        await repository.DeleteAsync(aggregateRoot, TestContext.Current.CancellationToken);

        context.Entry(aggregateRoot).State.Should().Be(EntityState.Detached);
        var aggregateRootExists = await context.Aggregates
            .AsNoTracking()
            .AnyAsync(item => item.Id == aggregateRoot.Id, TestContext.Current.CancellationToken);
        aggregateRootExists.Should().BeFalse();
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "EfRepository exposes base query and DbSet to derived repositories")]
    public async Task EfRepository_ExposesBaseQueryAndDbSetToDerivedRepositories()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedWriteRepository(context);

        var query = repository.QueryForTest;
        var dbSet = repository.DbSetForTest;

        query.Expression.Should().NotBeNull();
        dbSet.Should().BeSameAs(context.Aggregates);
    }

    [Fact(DisplayName = "AggregateTracker returns tracked roots and can clear them")]
    public async Task AggregateTracker_ReturnsTrackedRootsAndCanClearThem()
    {
        await using var context = await CreateContextAsync();
        var tracker = new AggregateTracker([context]);
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New());

        tracker.Track(aggregateRoot);
        tracker.Clear();

        tracker.GetAll().Should().BeEmpty();
    }

    private Task<TestWriteDbContext> CreateContextAsync()
    {
        return fixture.CreateInitializedDbContextAsync<TestWriteDbContext>(
            options => new TestWriteDbContext(options, []));
    }
}
