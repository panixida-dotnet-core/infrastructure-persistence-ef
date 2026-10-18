using System.Data.Common;
using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Messaging.EventBus;
using PANiXiDA.Core.Application.Messaging.Mediator.Behaviors;
using PANiXiDA.Core.Application.Messaging.Mediator.Contracts;
using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Application.Persistence.Errors;
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
    [Theory(DisplayName = "PublishDomainEventsBehavior publishes subsequent events including physically deleted aggregates")]
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

    [Theory(DisplayName = "AggregateTracker retains hard-deleted roots and publishes their events regardless of resolution order")]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task GetAll_RetainsHardDeletedRoots(bool async, bool acceptLater, bool resolveBeforeSave)
    {
        // Arrange
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
        var aggregate = await context.Set<DeletedAggregate>().SingleAsync(TestContext.Current.CancellationToken);
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
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
        aggregate.GetDomainEvents().Should().ContainSingle().Which.Should().BeOfType<DeletedEvent>();
        tracker.Track(aggregate);
        context.Entry(aggregate).State.Should().Be(EntityState.Detached);
        (await context.Set<DeletedAggregate>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
        var eventBus = new TestEventBus();
        var behavior = new PublishDomainEventsBehavior<TestCommand, Result>(eventBus, tracker);
        await behavior.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);
        await behavior.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);
        eventBus.PublishedEvents.Should().ContainSingle().Which.Should().BeOfType<DeletedEvent>();
        aggregate.GetDomainEvents().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
    }

    [Theory(DisplayName = "Clearing or disposing the tracker releases physically deleted roots while the context stays alive")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cleanup_ReleasesDeletedRoots(bool dispose)
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        var reference = SaveAndDeleteAggregate(context, tracker);

        if (dispose)
        {
            ((IDisposable)tracker).Dispose();
        }
        else
        {
            tracker.Clear();
        }
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.TryGetTarget(out _).Should().BeFalse();
        GC.KeepAlive(context);
        GC.KeepAlive(tracker);
    }

    [Theory(DisplayName = "Failed or canceled deletions do not publish stale events on a subsequent successful save")]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveFailure_DoesNotPublishStaleEvents(bool canceled, bool async)
    {
        var services = new ServiceCollection();
        var cancellationInterceptor = new CancelCommandInterceptor();
        services.AddSingleton<IInterceptor>(cancellationInterceptor);
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        cancellationInterceptor.Cancel = canceled && !async;
        await FailDeletionAsync(context, canceled, async);
        cancellationInterceptor.Cancel = false;
        tracker.GetAll().Should().BeEmpty();
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        aggregate.RaiseChangedEvent();
        context.Add(aggregate);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var eventBus = new TestEventBus();
        var behavior = new PublishDomainEventsBehavior<TestCommand, Result>(eventBus, tracker);
        await behavior.AfterAsync(new TestCommand(), Result.Success(), TestContext.Current.CancellationToken);

        eventBus.PublishedEvents.Should().ContainSingle().Which.Should().BeOfType<ChangedEvent>();
        tracker.GetAll().Should().BeEmpty();
    }

    [Theory(DisplayName = "Clearing during save completion does not restore deleted aggregates afterward")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Clear_DuringSaveCompletionDoesNotRestoreDeletedRoots(bool async)
    {
        // Arrange
        var services = new ServiceCollection();
        var completionInterceptor = new SaveCompletionInterceptor();
        services.AddSingleton<IInterceptor>(completionInterceptor);
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        context.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        completionInterceptor.OnSaved = tracker.Clear;
        aggregate.RaiseDeletedEvent();
        context.Remove(aggregate);

        // Act
        if (async)
        {
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        else
        {
            context.SaveChanges();
        }

        // Assert
        context.ChangeTracker.Entries().Should().BeEmpty();
        tracker.GetAll().Should().BeEmpty();
        (await context.Set<DeletedAggregate>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [Fact(DisplayName = "Saving after tracker disposal does not reactivate deletion capture")]
    public async Task Dispose_PreventsFurtherCapture()
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        ((IDisposable)tracker).Dispose();

        var reference = SaveAndDeleteAggregate(context, tracker, false);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.TryGetTarget(out _).Should().BeFalse();
        GC.KeepAlive(context);
        GC.KeepAlive(tracker);
    }

    [Fact(DisplayName = "Soft-deleted roots use normal EF tracking without being retained as physical deletions")]
    public async Task GetAll_DoesNotRetainSoftDeletedRoots()
    {
        var services = new ServiceCollection();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var aggregate = new SoftDeletedAggregate(TestAggregateRootId.New());
        context.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var tracker = scope.ServiceProvider.GetRequiredService<IAggregateTracker>();
        aggregate.RaiseEvent();

        context.Remove(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        aggregate.DeletedAt.Should().NotBeNull();
        context.Entry(aggregate).State.Should().Be(EntityState.Unchanged);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
        context.Entry(aggregate).State = EntityState.Detached;
        tracker.GetAll().Should().BeEmpty();
    }

    [Fact(DisplayName = "A custom aggregate tracker can be used without EF calling its Track method")]
    public async Task SavingChanges_PreservesCustomTracker()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAggregateTracker, CustomTracker>();
        services.AddPostgreSqlWriteEfRepository<DeletionWriteDbContext>(fixture.CreateConfiguration());
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<DeletionWriteDbContext>();
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        var repository = new EventRepository(context);

        await repository.AddAsync(aggregate, TestContext.Current.CancellationToken);
        await repository.DeleteAsync(aggregate, TestContext.Current.CancellationToken);

        scope.ServiceProvider.GetRequiredService<IAggregateTracker>().Should().BeOfType<CustomTracker>();
        (await context.Set<DeletedAggregate>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<DeletedAggregate> SaveAndDeleteAggregate(
        DeletionWriteDbContext context,
        IAggregateTracker tracker,
        bool retained = true)
    {
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        context.Add(aggregate);
        context.SaveChanges();
        aggregate.RaiseDeletedEvent();
        context.Remove(aggregate);
        context.SaveChanges();
        tracker.GetAll().Should().HaveCount(retained ? 1 : 0);
        return new WeakReference<DeletedAggregate>(aggregate);
    }

    private static async Task FailDeletionAsync(DeletionWriteDbContext context, bool canceled, bool async)
    {
        var aggregate = new DeletedAggregate(TestAggregateRootId.New());
        aggregate.RaiseDeletedEvent();
        context.Remove(aggregate);
        try
        {
            if (canceled && async)
            {
                using var cancellation = new CancellationTokenSource();
                cancellation.Cancel();
                await context.SaveChangesAsync(cancellation.Token);
            }
            else
            {
                context.SaveChanges();
            }
        }
        catch (Exception exception) when (canceled ? exception is OperationCanceledException : exception is ConcurrencyException)
        {
            context.ChangeTracker.Clear();
            return;
        }

        throw new InvalidOperationException("Saving was expected to fail.");
    }

    public enum RootChange { None, Update, Delete }

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
            modelBuilder.Entity<SoftDeletedAggregate>(builder =>
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

    private sealed class SoftDeletedAggregate(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
    {
        public DateTime? DeletedAt { get; set; }

        public void RaiseEvent() => AddDomainEvent(new DeletedEvent());
    }

    private sealed class CustomTracker : IAggregateTracker
    {
        public void Track(IAggregateRoot aggregateRoot) => throw new InvalidOperationException("Explicit Track must not be called.");

        public IReadOnlyCollection<IAggregateRoot> GetAll() => [];

        public void Clear() { }
    }

    private sealed class CancelCommandInterceptor : DbCommandInterceptor
    {
        public bool Cancel { get; set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            if (Cancel)
            {
                throw new OperationCanceledException();
            }

            return result;
        }
    }

    private sealed class SaveCompletionInterceptor : SaveChangesInterceptor
    {
        public Action? OnSaved { get; set; }

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            OnSaved?.Invoke();
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default)
        {
            OnSaved?.Invoke();
            return ValueTask.FromResult(result);
        }
    }

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
