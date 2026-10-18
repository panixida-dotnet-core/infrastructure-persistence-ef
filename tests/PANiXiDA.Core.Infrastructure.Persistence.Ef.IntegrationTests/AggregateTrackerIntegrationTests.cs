using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.EventBus;
using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.DomainEvents;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;
using PANiXiDA.Core.ResultPattern;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AggregateTrackerIntegrationTests(PostgreSqlContainerFixture fixture)
{
    [Theory(DisplayName = "PublishDomainEventsBehavior publishes subsequent events from the same EF aggregate without Track")]
    [InlineData(RootChange.None, false)]
    [InlineData(RootChange.None, true)]
    [InlineData(RootChange.Update, false)]
    [InlineData(RootChange.Update, true)]
    [InlineData(RootChange.Delete, false)]
    [InlineData(RootChange.Delete, true)]
    public async Task Behavior_PublishesNewEventsAfterClear(RootChange change, bool failedFirstCommand)
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        var repository = new EventRepository(context);
        await repository.AddAsync(aggregate, TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        var eventBus = new TestEventBus();
        var behavior = new PublishDomainEventsBehavior<TestCommand, Result>(eventBus, tracker);
        aggregate.RaiseChangedEvent();
        var firstResult = failedFirstCommand ? Result.Failure(Error.Validation("Command failed.")) : Result.Success();
        await behavior.AfterAsync(new TestCommand(), firstResult, TestContext.Current.CancellationToken);
        tracker.GetAll().Should().BeEmpty();
        var version = context.Entry(aggregate).Property<uint>("xmin").OriginalValue;

        // Act
        aggregate.RaiseChangedEvent();
        if (change == RootChange.Update)
        {
            aggregate.Name = "Changed";
            await repository.UpdateAsync(aggregate, TestContext.Current.CancellationToken);
        }
        else if (change == RootChange.Delete)
        {
            await repository.DeleteAsync(aggregate, TestContext.Current.CancellationToken);
        }
        await behavior.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);
        await behavior.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);

        // Assert
        eventBus.PublishedEvents.Should().HaveCount(failedFirstCommand ? 1 : 2).And.OnlyHaveUniqueItems();
        aggregate.GetDomainEvents().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
        context.Entry(aggregate).State.Should().Be(change == RootChange.Delete ? EntityState.Detached : EntityState.Unchanged);
        if (change == RootChange.None)
        {
            context.Entry(aggregate).Property<uint>("xmin").OriginalValue.Should().Be(version);
        }
    }

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
        tracker.Track(aggregate);
        context.Entry(aggregate).State.Should().Be(EntityState.Detached);
        (await context.Set<DeletedAggregate>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        tracker.Clear();
        tracker.GetAll().Should().BeEmpty();
    }

    public enum RootChange { None, Update, Delete }

    private sealed class EventRepository(DeletionWriteDbContext context)
        : EfRepository<DeletionWriteDbContext, TestAggregateRootId, DeletedAggregate>(context);

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
        public string Name { get; set; } = string.Empty;

        public void RaiseDeletedEvent() => AddDomainEvent(new DeletedEvent());

        public void RaiseChangedEvent() => AddDomainEvent(new ChangedEvent());
    }

    private sealed record DeletedEvent : DomainEvent;

    private sealed record ChangedEvent : DomainEvent;

    private sealed record TestCommand : ICommand<Result>;

    private sealed class TestEventBus : IEventBus
    {
        public List<DomainEvent> PublishedEvents { get; } = [];

        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken) where TEvent : DomainEvent
        {
            PublishedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }
}
