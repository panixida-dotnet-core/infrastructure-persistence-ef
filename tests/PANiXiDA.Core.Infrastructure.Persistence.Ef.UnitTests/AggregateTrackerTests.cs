using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class AggregateTrackerTests
{
    [Fact(DisplayName = "AggregateTracker tracks aggregate roots once by reference")]
    public void Track_TracksAggregateRootsOnce_ByReference()
    {
        var tracker = new AggregateTracker();
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New());

        tracker.Track(aggregateRoot);
        tracker.Track(aggregateRoot);

        tracker.GetAll().Should().ContainSingle().Which.Should().BeSameAs(aggregateRoot);
    }

    [Fact(DisplayName = "AggregateTracker returns snapshot of tracked aggregate roots")]
    public void GetAll_ReturnsSnapshotOfTrackedAggregateRoots()
    {
        var tracker = new AggregateTracker();
        var firstAggregateRoot = new TestAggregateRoot(TestAggregateRootId.New());
        var secondAggregateRoot = new TestAggregateRoot(TestAggregateRootId.New());

        tracker.Track(firstAggregateRoot);
        var trackedAggregateRoots = tracker.GetAll();
        tracker.Track(secondAggregateRoot);

        trackedAggregateRoots.Should().ContainSingle().Which.Should().BeSameAs(firstAggregateRoot);
        tracker.GetAll().Should().HaveCount(2);
    }

    [Fact(DisplayName = "AggregateTracker Clear removes tracked aggregate roots")]
    public void Clear_RemovesTrackedAggregateRoots()
    {
        var tracker = new AggregateTracker();
        var aggregateRoot = new TestAggregateRoot(TestAggregateRootId.New());

        tracker.Track(aggregateRoot);
        tracker.Clear();

        tracker.GetAll().Should().BeEmpty();
    }

    [Fact(DisplayName = "AggregateTracker Track throws when aggregate root is null")]
    public void Track_Throws_WhenAggregateRootIsNull()
    {
        var tracker = new AggregateTracker();

        var act = () => tracker.Track(null!);

        act.Should()
            .Throw<ArgumentNullException>()
            .WithParameterName("aggregateRoot");
    }
}
