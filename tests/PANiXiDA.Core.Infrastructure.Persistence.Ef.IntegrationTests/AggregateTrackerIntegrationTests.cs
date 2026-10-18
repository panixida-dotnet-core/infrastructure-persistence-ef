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
    [Theory(DisplayName = "PublishDomainEventsBehavior collects subsequent events only while EF tracks the aggregate")]
    [InlineData(RootChange.None, false)]
    [InlineData(RootChange.None, true)]
    [InlineData(RootChange.Update, false)]
    [InlineData(RootChange.Update, true)]
    [InlineData(RootChange.Delete, false)]
    [InlineData(RootChange.Delete, true)]
    public async Task Behavior_CollectsEventsOnlyFromEfTrackedRoots(RootChange change, bool failedFirstCommand)
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
        var expectedCount = failedFirstCommand ? 0 : 1;
        if (change == RootChange.Delete)
        {
            aggregate.GetDomainEvents().Should().ContainSingle();
        }
        else
        {
            expectedCount++;
            aggregate.GetDomainEvents().Should().BeEmpty();
        }
        eventBus.PublishedEvents.Should().HaveCount(expectedCount).And.OnlyHaveUniqueItems();
        tracker.GetAll().Should().BeEmpty();
        context.Entry(aggregate).State.Should().Be(change == RootChange.Delete ? EntityState.Detached : EntityState.Unchanged);
        if (change == RootChange.None)
        {
            context.Entry(aggregate).Property<uint>("xmin").OriginalValue.Should().Be(version);
        }
    }

    [Theory(DisplayName = "AggregateTracker excludes hard-deleted roots after EF accepts changes regardless of resolution order")]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task GetAll_ExcludesHardDeletedRoots(bool async, bool acceptLater, bool resolveBeforeSave)
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
        var tracker = resolveBeforeSave ? scope.ServiceProvider.GetRequiredService<IAggregateTracker>() : null;
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
        tracker ??= scope.ServiceProvider.GetRequiredService<IAggregateTracker>();

        // Assert
        context.ChangeTracker.Entries().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
        aggregate.GetDomainEvents().Should().ContainSingle().Which.Should().BeOfType<DeletedEvent>();
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
