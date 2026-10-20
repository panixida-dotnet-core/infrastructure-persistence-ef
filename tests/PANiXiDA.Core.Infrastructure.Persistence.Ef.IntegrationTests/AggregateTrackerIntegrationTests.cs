using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    [Theory(DisplayName = "The command pipeline collects deletion events before saving and skips failed commands")]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Pipeline_CollectsDeletionsBeforeSave(bool resolveBeforeDelete, bool failed, bool useTransaction)
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using (var seedScope = provider.CreateAsyncScope())
        {
            var seed = seedScope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
            await seed.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
            seed.Add(new DeletedAggregate(TestAggregateRootId.New()));
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(DeletionWriteDbContext));
        if (useTransaction)
        {
            await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        }
        var aggregate = await context.Set<DeletedAggregate>().SingleAsync(TestContext.Current.CancellationToken);
        var tracker = resolveBeforeDelete ? scope.ServiceProvider.GetRequiredService<IAggregateTracker>() : null;
        aggregate.RaiseEvent();
        new EventRepository(context).Delete(aggregate);
        tracker ??= scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        var eventBus = new TestEventBus();
        var publish = new PublishDomainEventsBehavior<TestCommand, Result>(eventBus, tracker);
        var result = failed ? Result.Failure(Error.Validation("Rejected.")) : Result.Success();

        context.Entry(aggregate).State.Should().Be(EntityState.Deleted);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
        (await context.Set<DeletedAggregate>().AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).Should().Be(1);
        await publish.AfterAsync(new TestCommand(), result, TestContext.Current.CancellationToken);
        context.Entry(aggregate).State.Should().Be(EntityState.Deleted);
        eventBus.PublishedEvents.Should().HaveCount(failed ? 0 : 1);
        await new SaveChangesBehavior<TestCommand, Result>(unitOfWork)
            .AfterAsync(new TestCommand(), result, TestContext.Current.CancellationToken);
        unitOfWork.HasActiveTransaction.Should().Be(useTransaction);
        await new CommitTransactionBehavior<TestCommand, Result>(unitOfWork)
            .AfterAsync(new TestCommand(), result, TestContext.Current.CancellationToken);
        await new CleanupTransactionBehavior<TestCommand, Result>(unitOfWork)
            .FinallyAsync(new TestCommand(), result, null, TestContext.Current.CancellationToken);

        (await context.Set<DeletedAggregate>().AsNoTracking().CountAsync(TestContext.Current.CancellationToken)).Should().Be(failed ? 1 : 0);
        context.Entry(aggregate).State.Should().Be(failed ? EntityState.Deleted : EntityState.Detached);
        await publish.AfterAsync(new TestCommand(), result, TestContext.Current.CancellationToken);
        eventBus.PublishedEvents.Should().HaveCount(failed ? 0 : 1);
        aggregate.GetDomainEvents().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
    }

    [Fact(DisplayName = "New events on a cleared root are collected before deferred deletion without explicit Track")]
    public async Task Pipeline_RediscoversDeletedRootAfterClear()
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var unitOfWork = scope.ServiceProvider.GetRequiredKeyedService<IUnitOfWork>(typeof(DeletionWriteDbContext));
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        var repository = new EventRepository(context);
        repository.Add(aggregate);
        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        var eventBus = new TestEventBus();
        var publish = new PublishDomainEventsBehavior<TestCommand, Result>(eventBus, tracker);
        aggregate.RaiseEvent();
        await publish.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);
        tracker.GetAll().Should().BeEmpty();
        var originalVersion = context.Entry(aggregate).Property<uint>("xmin").OriginalValue;

        aggregate.RaiseEvent();
        repository.Delete(aggregate);
        await publish.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);
        context.Entry(aggregate).Property<uint>("xmin").OriginalValue.Should().Be(originalVersion);
        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);
        await publish.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);

        eventBus.PublishedEvents.Should().HaveCount(2).And.OnlyHaveUniqueItems();
        aggregate.GetDomainEvents().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
        context.ChangeTracker.Entries().Should().BeEmpty();
        (await context.Set<DeletedAggregate>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    private sealed class EventRepository(DeletionWriteDbContext context)
        : EfRepository<DeletionWriteDbContext, TestAggregateRootId, DeletedAggregate>(context);

    private sealed class DeletionWriteDbContext(DbContextOptions<DeletionWriteDbContext> options, IEnumerable<IInterceptor> interceptors)
        : WriteDbContext<DeletionWriteDbContext>(options, interceptors)
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
        public void RaiseEvent() => AddDomainEvent(new DeletedEvent());
    }

    private sealed record DeletedEvent : DomainEvent;

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
