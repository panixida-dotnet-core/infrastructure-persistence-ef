using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.TestDoubles;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfWriteRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "EfWriteRepository Add marks aggregate for insert and tracks it")]
    public async Task Add_MarksAggregateForInsertAndTracksIt()
    {
        await using var context = await CreateContextAsync();
        var tracker = new FakeAggregateTracker();
        var repository = new ExposedWriteRepository(context, tracker);
        var aggregateRoot = new TestAggregateRoot(1)
        {
            Name = "Created"
        };

        repository.Add(aggregateRoot);

        context.Entry(aggregateRoot).State.Should().Be(EntityState.Added);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "EfWriteRepository GetByIdAsync returns detached aggregate when found")]
    public async Task GetByIdAsync_ReturnsDetachedAggregate_WhenFound()
    {
        await using var context = await CreateContextAsync();
        context.Aggregates.Add(new TestAggregateRoot(1)
        {
            Name = "Stored"
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var repository = new ExposedWriteRepository(context, new FakeAggregateTracker());

        var aggregateRoot = await repository.GetByIdAsync(1, TestContext.Current.CancellationToken);

        aggregateRoot.Should().NotBeNull();
        aggregateRoot!.Name.Should().Be("Stored");
        context.Entry(aggregateRoot).State.Should().Be(EntityState.Detached);
    }

    [Fact(DisplayName = "EfWriteRepository GetByIdAsync returns null when aggregate is not found")]
    public async Task GetByIdAsync_ReturnsNull_WhenAggregateIsNotFound()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedWriteRepository(context, new FakeAggregateTracker());

        var aggregateRoot = await repository.GetByIdAsync(404, TestContext.Current.CancellationToken);

        aggregateRoot.Should().BeNull();
    }

    [Fact(DisplayName = "EfWriteRepository Update marks aggregate for update and tracks it")]
    public async Task Update_MarksAggregateForUpdateAndTracksIt()
    {
        await using var context = await CreateContextAsync();
        var aggregateRoot = new TestAggregateRoot(1)
        {
            Name = "Stored"
        };
        context.Aggregates.Add(aggregateRoot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();

        var tracker = new FakeAggregateTracker();
        var repository = new ExposedWriteRepository(context, tracker);
        aggregateRoot.Name = "Updated";

        repository.Update(aggregateRoot);

        context.Entry(aggregateRoot).State.Should().Be(EntityState.Modified);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "EfWriteRepository Delete marks aggregate for deletion and tracks it")]
    public async Task Delete_MarksAggregateForDeletionAndTracksIt()
    {
        await using var context = await CreateContextAsync();
        var aggregateRoot = new TestAggregateRoot(1)
        {
            Name = "Stored"
        };
        context.Aggregates.Add(aggregateRoot);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tracker = new FakeAggregateTracker();
        var repository = new ExposedWriteRepository(context, tracker);

        repository.Delete(aggregateRoot);

        context.Entry(aggregateRoot).State.Should().Be(EntityState.Deleted);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "EfWriteRepository exposes base query and DbSet to derived repositories")]
    public async Task EfWriteRepository_ExposesBaseQueryAndDbSetToDerivedRepositories()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedWriteRepository(context, new FakeAggregateTracker());

        var query = repository.QueryForTest;
        var dbSet = repository.DbSetForTest;

        query.Expression.Should().NotBeNull();
        dbSet.Should().BeSameAs(context.Aggregates);
    }

    [Fact(DisplayName = "FakeAggregateTracker returns tracked roots and can clear them")]
    public void FakeAggregateTracker_ReturnsTrackedRootsAndCanClearThem()
    {
        var tracker = new FakeAggregateTracker();
        var aggregateRoot = new TestAggregateRoot(1);

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
