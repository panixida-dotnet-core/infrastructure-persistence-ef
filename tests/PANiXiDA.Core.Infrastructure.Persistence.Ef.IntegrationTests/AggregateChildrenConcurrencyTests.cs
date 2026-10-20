using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class AggregateChildrenConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Theory(DisplayName = "Saving child changes through the root updates its audit timestamp and xmin")]
    [InlineData(ChildChange.Add)]
    [InlineData(ChildChange.Update)]
    [InlineData(ChildChange.Delete)]
    public async Task SaveChanges_ChildChangeRefreshesRootVersion(ChildChange change)
    {
        var clock = new TestTimeProvider();
        await using var context = await fixture.CreateInitializedDbContextAsync<ChildrenWriteDbContext>(options => new(options, clock));
        var aggregate = new ParentAggregate(TestAggregateRootId.New());
        aggregate.AddChild("Original");
        context.Add(aggregate);
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        var repository = new ChildrenRepository(context);
        var loaded = (await repository.GetByIdAsync(aggregate.Id, TestContext.Current.CancellationToken))!;
        var originalVersion = context.Entry(loaded).Property<uint>("xmin").OriginalValue;
        var originalUpdatedAt = context.Entry(loaded).Property<DateTime>("UpdatedAt").CurrentValue;
        clock.Advance();

        ChangeChild(loaded, change);
        repository.Update(loaded);
        context.Entry(loaded).Property<uint>("xmin").CurrentValue.Should().Be(originalVersion);
        await new EfUnitOfWork<ChildrenWriteDbContext>(context).SaveChangesAsync(TestContext.Current.CancellationToken);

        var savedVersion = context.Entry(loaded).Property<uint>("xmin").CurrentValue;
        savedVersion.Should().NotBe(originalVersion);
        context.Entry(loaded).Property<DateTime>("UpdatedAt").CurrentValue.Should().BeAfter(originalUpdatedAt);
        context.ChangeTracker.Clear();
        var stored = (await repository.GetByIdAsync(aggregate.Id, TestContext.Current.CancellationToken))!;
        context.Entry(stored).Property<uint>("xmin").CurrentValue.Should().Be(savedVersion);
        context.Entry(stored).Property<DateTime>("UpdatedAt").CurrentValue.Should().Be(clock.GetUtcNow().UtcDateTime);
        var expectedNames = change switch
        {
            ChildChange.Add => new[] { "Original", "Added" },
            ChildChange.Update => ["Updated"],
            ChildChange.Delete => [],
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        stored.Children.Select(child => child.Name).Should().BeEquivalentTo(expectedNames);
        (await context.Set<AggregateChild>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(expectedNames.Length);
    }

    [Theory(DisplayName = "A stale root rejects child changes and rolls back all changes from that save")]
    [InlineData(ChildChange.Add)]
    [InlineData(ChildChange.Update)]
    [InlineData(ChildChange.Delete)]
    public async Task SaveChanges_ChildChangeRejectsStaleRoot(ChildChange change)
    {
        var database = PostgreSqlContainerFixture.CreateDatabaseName();
        var clock = new TestTimeProvider();
        await using var stale = await fixture.CreateInitializedDbContextAsync<ChildrenWriteDbContext>(database, options => new(options, clock));
        var aggregate = new ParentAggregate(TestAggregateRootId.New());
        aggregate.AddChild("Original");
        stale.Add(aggregate);
        await stale.SaveChangesAsync(TestContext.Current.CancellationToken);
        stale.ChangeTracker.Clear();
        var staleRepository = new ChildrenRepository(stale);
        var loaded = (await staleRepository.GetByIdAsync(aggregate.Id, TestContext.Current.CancellationToken))!;
        var childVersion = stale.Entry(loaded.Children.Single()).Property<uint>("xmin").OriginalValue;

        await using var current = fixture.CreateDbContext<ChildrenWriteDbContext>(database, options => new(options, clock));
        var currentRepository = new ChildrenRepository(current);
        var winner = await current.Set<ParentAggregate>().SingleAsync(TestContext.Current.CancellationToken);
        winner.AddChild("Concurrent addition");
        clock.Advance();
        currentRepository.Update(winner);
        await new EfUnitOfWork<ChildrenWriteDbContext>(current).SaveChangesAsync(TestContext.Current.CancellationToken);
        var winnerVersion = current.Entry(winner).Property<uint>("xmin").CurrentValue;
        var winnerUpdatedAt = current.Entry(winner).Property<DateTime>("UpdatedAt").CurrentValue;
        ChangeChild(loaded, change);
        clock.Advance();

        staleRepository.Update(loaded);
        Func<Task> act = () => new EfUnitOfWork<ChildrenWriteDbContext>(stale).SaveChangesAsync(TestContext.Current.CancellationToken);

        var exception = (await act.Should().ThrowAsync<ConcurrencyException>()).Which;
        var inner = exception.InnerException.Should().BeOfType<DbUpdateConcurrencyException>().Subject;
        inner.Entries.Should().Contain(entry => ReferenceEquals(entry.Entity, loaded));
        current.ChangeTracker.Clear();
        var stored = (await currentRepository.GetByIdAsync(aggregate.Id, TestContext.Current.CancellationToken))!;
        stored.Children.Select(child => child.Name).Should().BeEquivalentTo(["Original", "Concurrent addition"]);
        (await current.Set<AggregateChild>().CountAsync(TestContext.Current.CancellationToken)).Should().Be(2);
        current.Entry(stored.Children.Single(child => child.Name == "Original")).Property<uint>("xmin").CurrentValue.Should().Be(childVersion);
        current.Entry(stored).Property<uint>("xmin").CurrentValue.Should().Be(winnerVersion);
        current.Entry(stored).Property<DateTime>("UpdatedAt").CurrentValue.Should().Be(winnerUpdatedAt);
    }

    private static void ChangeChild(ParentAggregate aggregate, ChildChange change)
    {
        switch (change)
        {
            case ChildChange.Add:
                aggregate.AddChild("Added");
                break;
            case ChildChange.Update:
                aggregate.RenameChild(aggregate.Children.Single().Id, "Updated");
                break;
            case ChildChange.Delete:
                aggregate.RemoveChild(aggregate.Children.Single().Id);
                break;
        }
    }

    public enum ChildChange { Add, Update, Delete }

    private sealed class TestTimeProvider : TimeProvider
    {
        private DateTimeOffset now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => now;

        public void Advance() => now = now.AddMinutes(1);
    }

    private sealed class ChildrenRepository(ChildrenWriteDbContext context)
        : EfRepository<ChildrenWriteDbContext, TestAggregateRootId, ParentAggregate>(context)
    {
        protected override IQueryable<ParentAggregate> Query => base.Query.Include(aggregate => aggregate.Children);
    }

    private sealed class ChildrenWriteDbContext(DbContextOptions<ChildrenWriteDbContext> options, TimeProvider clock)
        : WriteDbContext<ChildrenWriteDbContext>(options, [new AuditSaveChangesInterceptor(clock)])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfiguration(new ParentConfiguration("parent_aggregates"));
            modelBuilder.Entity<AggregateChild>().HasKey(child => child.Id);
        }
    }

    private sealed class ParentConfiguration(string tableName) : AuditableEntityConfiguration<ParentAggregate>
    {
        protected override void ConfigureEntity(EntityTypeBuilder<ParentAggregate> builder)
        {
            builder.ToTable(tableName);
            builder.HasKey(aggregate => aggregate.Id);
            builder.Property(aggregate => aggregate.Id).HasConversion(id => id.Value, value => new TestAggregateRootId(value));
            builder.HasMany(aggregate => aggregate.Children).WithOne().HasForeignKey("AggregateId").IsRequired().OnDelete(DeleteBehavior.Cascade);
        }
    }

    private sealed class ParentAggregate(TestAggregateRootId id) : AggregateRoot<TestAggregateRootId>(id)
    {
        public List<AggregateChild> Children { get; } = [];

        public void AddChild(string name) => Children.Add(new AggregateChild { Name = name });

        public void RenameChild(int id, string name) => Children.Single(child => child.Id == id).Name = name;

        public void RemoveChild(int id) => Children.Remove(Children.Single(child => child.Id == id));
    }

    private sealed class AggregateChild : IEntity
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }
}
