using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.DomainEvents;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AggregateTrackerIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory(DisplayName = "AggregateTracker resolved after saving retains hard-deleted roots and their domain events")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GetAll_RetainsDeletionBeforeTrackerResolution(bool async, bool acceptLater)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        context.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        aggregate.RaiseDeletedEvent();
        context.Remove(aggregate);

        // Act
        if (async)
        {
            await context.SaveChangesAsync(!acceptLater, TestContext.Current.CancellationToken);
        }
        else
        {
            context.SaveChanges(!acceptLater);
        }
        if (acceptLater)
        {
            context.ChangeTracker.AcceptAllChanges();
        }
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();

        // Assert
        context.ChangeTracker.Entries().Should().BeEmpty();
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
        tracker.GetAll().Single().GetDomainEvents().Should().ContainSingle().Which.Should().BeOfType<DeletedEvent>();
        (await context.Set<DeletedAggregate>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        tracker.Clear();
        tracker.GetAll().Should().BeEmpty();
    }

    private sealed class DeletionWriteDbContext(DbContextOptions<DeletionWriteDbContext> options)
        : WriteDbContext<DeletionWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DeletedAggregate>(builder =>
            {
                builder.HasKey(aggregate => aggregate.Id);
                builder.Property(aggregate => aggregate.Id).HasConversion(id => id.Value, value => new TestAggregateRootId(value));
            });
        }
    }

    private sealed class DeletedAggregate(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
    {
        public void RaiseDeletedEvent() => AddDomainEvent(new DeletedEvent());
    }

    private sealed record DeletedEvent : DomainEvent;
}
