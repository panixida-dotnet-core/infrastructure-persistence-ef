using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.DomainEvents;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class AggregateTrackerTests
{
    [Theory(DisplayName = "New events make a cleared EF aggregate visible without explicit tracking")]
    [InlineData(false)]
    [InlineData(true)]
    public void GetAll_RediscoversNewEventsAfterClear(bool clearEventsFirst)
    {
        // Arrange
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var aggregate = new MappedAggregate(TestAggregateRootId.New());
        context.Attach(aggregate);
        aggregate.RaiseEvent();
        if (clearEventsFirst)
        {
            aggregate.ClearDomainEvents();
        }
        tracker.Clear();
        tracker.GetAll().Should().BeEmpty();

        // Act
        aggregate.RaiseEvent();

        // Assert
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
        context.Entry(aggregate).State.Should().Be(EntityState.Unchanged);
        tracker.Clear();
        tracker.GetAll().Should().BeEmpty();
        aggregate.ClearDomainEvents();
        aggregate.RaiseEvent();
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
    }

    [Theory(DisplayName = "AggregateTracker resolves unmapped runtime subclasses of mapped aggregate types")]
    [InlineData(false)]
    [InlineData(true)]
    public void Track_ResolvesRuntimeType(bool attached)
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var aggregate = new RuntimeAggregate(TestAggregateRootId.New());
        if (attached)
        {
            context.Add(aggregate);
        }

        tracker.Track(aggregate);

        context.Entry(aggregate).State.Should().Be(attached ? EntityState.Added : EntityState.Unchanged);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
    }

    [Theory(DisplayName = "AggregateTracker Clear does not retain aggregates after EF releases them")]
    [InlineData(false)]
    [InlineData(true)]
    public void Clear_ReleasesDetachedAggregates(bool clearChangeTracker)
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var reference = TrackAndReleaseAggregate(context, tracker, clearChangeTracker);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.TryGetTarget(out _).Should().BeFalse();
        GC.KeepAlive(tracker);
        GC.KeepAlive(context);
    }

    [Fact(DisplayName = "AggregateTracker reads aggregates from EF without explicit Track calls")]
    public void GetAll_ReadsEfTrackedAggregates()
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var aggregate = new TestAggregateRoot(TestAggregateRootId.New());
        context.Add(aggregate);
        context.Add(new OtherEntity { Id = 1 });

        var aggregates = tracker.GetAll();

        aggregates.Should().ContainSingle().Which.Should().BeSameAs(aggregate);
    }

    [Fact(DisplayName = "AggregateTracker attaches detached aggregates and preserves tracked states")]
    public void Track_AttachesDetachedAggregateAndPreservesState()
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var aggregate = new TestAggregateRoot(TestAggregateRootId.New());

        tracker.Track(aggregate);
        context.Entry(aggregate).State.Should().Be(EntityState.Unchanged);
        context.Entry(aggregate).State = EntityState.Modified;
        tracker.Track(aggregate);

        context.Entry(aggregate).State.Should().Be(EntityState.Modified);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
    }

    [Fact(DisplayName = "AggregateTracker returns a snapshot across all supplied contexts")]
    public void GetAll_ReturnsSnapshotAcrossContexts()
    {
        using var first = new TrackerDbContext();
        using var second = new TrackerDbContext();
        using var tracker = new AggregateTracker([first, second, first]);
        var firstAggregate = new TestAggregateRoot(TestAggregateRootId.New());
        var secondAggregate = new TestAggregateRoot(TestAggregateRootId.New());
        first.Add(firstAggregate);

        var snapshot = tracker.GetAll();
        second.Add(secondAggregate);
        tracker.Track(secondAggregate);

        snapshot.Should().ContainSingle().Which.Should().BeSameAs(firstAggregate);
        tracker.GetAll().Should().BeEquivalentTo([firstAggregate, secondAggregate]);
    }

    [Theory(DisplayName = "AggregateTracker Clear preserves EF states and original values and allows retracking")]
    [InlineData(EntityState.Unchanged)]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void Clear_PreservesEfTrackingAndAllowsRetracking(EntityState state)
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var aggregate = new TestAggregateRoot(TestAggregateRootId.New());
        context.Entry(aggregate).Property<uint>("xmin").CurrentValue = 42;
        context.Attach(aggregate);
        context.Entry(aggregate).State = state;

        tracker.Clear();

        tracker.GetAll().Should().BeEmpty();
        context.Entry(aggregate).State.Should().Be(state);
        context.Entry(aggregate).Property<uint>("xmin").OriginalValue.Should().Be(42);
        tracker.Track(aggregate);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
    }

    [Fact(DisplayName = "AggregateTracker follows EF detachment without treating manual state changes as saved deletions")]
    public void GetAll_FollowsEfDetachment()
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var detached = new TestAggregateRoot(TestAggregateRootId.New());
        var deleted = new TestAggregateRoot(TestAggregateRootId.New());
        context.AttachRange(detached, deleted);

        context.Entry(detached).State = EntityState.Detached;
        context.Remove(deleted);
        context.ChangeTracker.AcceptAllChanges();

        context.Entry(deleted).State.Should().Be(EntityState.Detached);
        tracker.GetAll().Should().BeEmpty();
    }

    [Theory(DisplayName = "AggregateTracker rejects detached aggregates without exactly one matching context")]
    [InlineData(false)]
    [InlineData(true)]
    public void Track_Throws_WhenContextCannotBeSelected(bool ambiguous)
    {
        using var first = new TrackerDbContext();
        using var second = new TrackerDbContext();
        using var tracker = new AggregateTracker(ambiguous ? [first, second] : []);

        var act = () => tracker.Track(new TestAggregateRoot(TestAggregateRootId.New()));

        act.Should().Throw<InvalidOperationException>().WithMessage("*exactly one write DbContext*");
    }

    [Fact(DisplayName = "AggregateTracker rejects null entries in the context collection")]
    public void Constructor_RejectsNullContextEntries()
    {
        var nullContext = () => new AggregateTracker([null!]);

        nullContext.Should().Throw<ArgumentException>().WithParameterName("dbContexts");
    }

    [Fact(DisplayName = "AggregateTracker Dispose does not dispose contexts")]
    public void Dispose_DoesNotDisposeContexts()
    {
        using var context = new TrackerDbContext();
        var tracker = new AggregateTracker([context]);
        var aggregate = new TestAggregateRoot(TestAggregateRootId.New());
        context.Attach(aggregate);

        tracker.Dispose();
        context.Remove(aggregate);
        context.ChangeTracker.AcceptAllChanges();

        tracker.GetAll().Should().BeEmpty();
        context.Add(new TestAggregateRoot(TestAggregateRootId.New()));
        context.ChangeTracker.Entries().Should().ContainSingle();
    }

    [Fact(DisplayName = "Disposing an aggregate tracker removes all subscriptions to a live context")]
    public void Dispose_ReleasesTrackerWhileContextRemainsAlive()
    {
        using var context = new TrackerDbContext();
        var reference = CreateAndDisposeTracker(context);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        reference.TryGetTarget(out _).Should().BeFalse();
        GC.KeepAlive(context);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<AggregateTracker> CreateAndDisposeTracker(TrackerDbContext context)
    {
        var tracker = new AggregateTracker([context]);
        tracker.Dispose();
        return new WeakReference<AggregateTracker>(tracker);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<TestAggregateRoot> TrackAndReleaseAggregate(
        TrackerDbContext context,
        AggregateTracker tracker,
        bool clearChangeTracker)
    {
        var aggregate = new TestAggregateRoot(TestAggregateRootId.New());
        context.Attach(aggregate);
        tracker.Clear();
        if (clearChangeTracker)
        {
            context.ChangeTracker.Clear();
        }
        else
        {
            context.Entry(aggregate).State = EntityState.Detached;
        }

        return new WeakReference<TestAggregateRoot>(aggregate);
    }

    private class MappedAggregate(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
    {
        public void RaiseEvent() => AddDomainEvent(new ChangedEvent());
    }

    private sealed record ChangedEvent : DomainEvent;

    private sealed class RuntimeAggregate(TestAggregateRootId id) : MappedAggregate(id);

    private sealed class TrackerDbContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=tracker_model");
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<TestAggregateRoot>(builder =>
            {
                builder.HasKey(entity => entity.Id);
                builder.Property(entity => entity.Id).HasConversion(id => id.Value, value => new TestAggregateRootId(value));
                builder.Property<uint>("xmin").IsRowVersion();
            });
            modelBuilder.Entity<OtherEntity>().HasKey(entity => entity.Id);
            modelBuilder.Entity<MappedAggregate>(builder =>
            {
                builder.HasKey(entity => entity.Id);
                builder.Property(entity => entity.Id).HasConversion(id => id.Value, value => new TestAggregateRootId(value));
            });
        }
    }

    private sealed class OtherEntity
    {
        public int Id { get; set; }
    }
}
