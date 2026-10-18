using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Domain.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class XminConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "Automatic xmin reuses explicit row versions and supports inherited and shared-row entity mappings")]
    public async Task Model_PreservesExplicitVersionsAndSharedRows()
    {
        await using var context = await fixture.CreateInitializedDbContextAsync<MappingWriteDbContext>(options => new(options));
        var explicitEntity = new ExplicitVersionEntity { Name = "Original" };
        var inherited = new DerivedEntity { Name = "Inherited" };
        var owner = new SharedRowEntity { Details = new SharedDetails { Name = "Owned" } };
        context.AddRange(explicitEntity, inherited, owner);

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        var version = explicitEntity.Version;
        owner.Details.Name = "Changed";
        explicitEntity.Name = "Changed";
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        version.Should().NotBe(0);
        explicitEntity.Version.Should().NotBe(version);
        context.Model.FindEntityType(typeof(ExplicitVersionEntity))!.FindProperty("xmin").Should().BeNull();
        context.Model.FindEntityType(typeof(DerivedEntity))!.GetProperty("xmin").DeclaringType.ClrType.Should().Be(typeof(BaseEntity));
        context.Model.FindEntityType(typeof(JsonDetails))!.FindProperty("xmin").Should().BeNull();
        context.Model.FindEntityType(typeof(KeylessDomainEntity))!.FindProperty("xmin").Should().BeNull();
        context.Model.FindEntityType(typeof(DomainView))!.FindProperty("xmin").Should().BeNull();
        context.Entry(owner).Property<uint>("xmin").CurrentValue.Should().NotBe(0);
        context.Entry(owner.Details).Property<uint>("xmin").CurrentValue.Should().Be(context.Entry(owner).Property<uint>("xmin").CurrentValue);
    }

    [Fact(DisplayName = "Write context automatically maps xmin for aggregates and domain entities configured after the base model")]
    public async Task Model_MapsXminForDomainEntities()
    {
        await using var context = await CreateContextAsync();

        var aggregateToken = context.Model.FindEntityType(typeof(TestAggregateRoot))!.FindProperty("xmin");
        var entityToken = context.Model.FindEntityType(typeof(ChildEntity))!.FindProperty("xmin");

        foreach (var property in new[] { aggregateToken, entityToken })
        {
            property.Should().NotBeNull();
            property!.ClrType.Should().Be(typeof(uint));
            property.IsConcurrencyToken.Should().BeTrue();
            property.IsShadowProperty().Should().BeTrue();
            property.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
            property.GetColumnName().Should().Be("xmin");
        }
        context.Model.FindEntityType(typeof(NonAuditableEntity))!.FindProperty("xmin").Should().BeNull();
        context.Database.GenerateCreateScript().Should().NotContain("\"xmin\"");
    }

    [Fact(DisplayName = "Repository updates a tracked aggregate and refreshes its shadow xmin")]
    public async Task UpdateAsync_RefreshesShadowXmin()
    {
        await using var context = await CreateContextAsync();
        var repository = new XminRepository(context, new AggregateTracker([context]));
        var entity = new TestAggregateRoot(TestAggregateRootId.New()) { Name = "Original" };
        await repository.AddAsync(entity, TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        var loaded = (await repository.GetByIdAsync(entity.Id, TestContext.Current.CancellationToken))!;
        var originalVersion = context.Entry(loaded).Property<uint>("xmin").OriginalValue;

        loaded.Name = "Updated";
        await repository.UpdateAsync(loaded, TestContext.Current.CancellationToken);

        originalVersion.Should().NotBe(0);
        context.Entry(loaded).Property<uint>("xmin").CurrentValue.Should().NotBe(originalVersion);
        context.ChangeTracker.Clear();
        (await repository.GetByIdAsync(entity.Id, TestContext.Current.CancellationToken))!.Name.Should().Be("Updated");
    }

    [Theory(DisplayName = "Repository rejects stale aggregate updates and deletions using automatic xmin")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repository_RejectsStaleVersion(bool delete)
    {
        var database = PostgreSqlContainerFixture.CreateDatabaseName();
        await using var stale = await fixture.CreateInitializedDbContextAsync<XminWriteDbContext>(database, options => new(options));
        var entity = new TestAggregateRoot(TestAggregateRootId.New()) { Name = "Original" };
        stale.Add(entity);
        await stale.SaveChangesAsync(TestContext.Current.CancellationToken);
        stale.ChangeTracker.Clear();
        var repository = new XminRepository(stale, new AggregateTracker([stale]));
        var loaded = (await repository.GetByIdAsync(entity.Id, TestContext.Current.CancellationToken))!;
        await using var current = fixture.CreateDbContext<XminWriteDbContext>(database, options => new(options));
        var changed = await current.Set<TestAggregateRoot>().SingleAsync(TestContext.Current.CancellationToken);
        changed.Name = "Concurrent update";
        await current.SaveChangesAsync(TestContext.Current.CancellationToken);
        loaded.Name = "Stale update";

        Func<Task> act = () => delete
            ? repository.DeleteAsync(loaded, TestContext.Current.CancellationToken)
            : repository.UpdateAsync(loaded, TestContext.Current.CancellationToken);

        (await act.Should().ThrowAsync<ConcurrencyException>()).Which.InnerException.Should().BeOfType<DbUpdateConcurrencyException>();
        current.ChangeTracker.Clear();
        (await current.Set<TestAggregateRoot>().SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be("Concurrent update");
    }

    [Fact(DisplayName = "Automatic xmin rejects concurrent domain entity updates without an aggregate repository")]
    public async Task SaveChanges_RejectsStaleDomainEntity()
    {
        var database = PostgreSqlContainerFixture.CreateDatabaseName();
        await using var stale = await fixture.CreateInitializedDbContextAsync<XminWriteDbContext>(database, options => new(options));
        var entity = new ChildEntity(TestAggregateRootId.New()) { Name = "Original" };
        stale.Add(entity);
        await stale.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var current = fixture.CreateDbContext<XminWriteDbContext>(database, options => new(options));
        var changed = await current.Set<ChildEntity>().SingleAsync(TestContext.Current.CancellationToken);
        changed.Name = "Concurrent update";
        await current.SaveChangesAsync(TestContext.Current.CancellationToken);
        entity.Name = "Stale update";

        Func<Task> act = () => stale.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConcurrencyException>();
    }

    private Task<XminWriteDbContext> CreateContextAsync()
    {
        return fixture.CreateInitializedDbContextAsync<XminWriteDbContext>(options => new(options));
    }

    private sealed class XminRepository(XminWriteDbContext context, AggregateTracker tracker)
        : EfRepository<XminWriteDbContext, TestAggregateRootId, TestAggregateRoot>(context, tracker);

    private sealed class XminWriteDbContext(DbContextOptions<XminWriteDbContext> options)
        : WriteDbContext<XminWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<ChildEntity>(builder =>
            {
                builder.HasKey(entity => entity.Id);
                builder.Property(entity => entity.Id).HasConversion(id => id.Value, value => new TestAggregateRootId(value));
            });
        }
    }

    private sealed class ChildEntity(TestAggregateRootId id) : Entity<TestAggregateRootId>(id)
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MappingWriteDbContext(DbContextOptions<MappingWriteDbContext> options)
        : WriteDbContext<MappingWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<ExplicitVersionEntity>().Property(entity => entity.Version).IsRowVersion();
            modelBuilder.Entity<BaseEntity>().HasDiscriminator<string>("kind")
                .HasValue<BaseEntity>("base").HasValue<DerivedEntity>("derived");
            modelBuilder.Entity<SharedRowEntity>().OwnsOne(entity => entity.Details);
            modelBuilder.Entity<SharedRowEntity>().OwnsOne(entity => entity.Value);
            modelBuilder.Entity<SharedRowEntity>().OwnsOne(entity => entity.Json).ToJson();
            modelBuilder.Entity<KeylessDomainEntity>().HasNoKey().ToView("domain_report");
            modelBuilder.Entity<DomainView>().ToView("domain_view");
        }
    }

    private sealed class ExplicitVersionEntity : IEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public uint Version { get; set; }
    }

    private class BaseEntity : IEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DerivedEntity : BaseEntity;

    private sealed class SharedRowEntity : IEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public SharedDetails Details { get; set; } = new();
        public ValueDetails Value { get; set; } = new();
        public JsonDetails Json { get; set; } = new();
    }

    private sealed class SharedDetails : IEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class ValueDetails
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class JsonDetails : IEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class KeylessDomainEntity : IEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class DomainView : IEntity
    {
        public int Id { get; set; }
    }
}
