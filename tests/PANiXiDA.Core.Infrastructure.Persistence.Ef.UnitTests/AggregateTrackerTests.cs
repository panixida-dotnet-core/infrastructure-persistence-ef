using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class AggregateTrackerTests
{
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

    [Fact(DisplayName = "AggregateTracker Clear preserves EF states and original values and allows retracking")]
    public void Clear_PreservesEfTrackingAndAllowsRetracking()
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);
        var aggregate = new TestAggregateRoot(TestAggregateRootId.New());
        context.Entry(aggregate).Property<uint>("xmin").CurrentValue = 42;
        context.Attach(aggregate);

        tracker.Clear();

        tracker.GetAll().Should().BeEmpty();
        context.Entry(aggregate).State.Should().Be(EntityState.Unchanged);
        context.Entry(aggregate).Property<uint>("xmin").OriginalValue.Should().Be(42);
        tracker.Track(aggregate);
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregate);
    }

    [Fact(DisplayName = "AggregateTracker follows EF detachment and retains accepted deletions until Clear")]
    public void GetAll_FollowsDetachmentAndRetainsDeletedAggregates()
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
        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(deleted);
        tracker.Track(deleted);
        context.Entry(deleted).State.Should().Be(EntityState.Detached);
        tracker.Clear();
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

    [Fact(DisplayName = "AggregateTracker validates null arguments")]
    public void Arguments_RejectNull()
    {
        using var context = new TrackerDbContext();
        using var tracker = new AggregateTracker([context]);

        var nullContexts = () => new AggregateTracker(null!);
        var nullContext = () => new AggregateTracker([null!]);
        var nullAggregate = () => tracker.Track(null!);

        nullContexts.Should().Throw<ArgumentNullException>().WithParameterName("dbContexts");
        nullContext.Should().Throw<ArgumentException>().WithParameterName("dbContexts");
        nullAggregate.Should().Throw<ArgumentNullException>().WithParameterName("aggregateRoot");
    }

    [Fact(DisplayName = "AggregateTracker Dispose unsubscribes without disposing contexts")]
    public void Dispose_UnsubscribesWithoutDisposingContexts()
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
        }
    }

    private sealed class OtherEntity
    {
        public int Id { get; set; }
    }
}
