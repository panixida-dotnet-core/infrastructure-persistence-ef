using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class XminConcurrencyTests(PostgreSqlContainerFixture fixture)
{
    [Theory(DisplayName = "Automatic xmin rejects entity splitting even with an explicit row version")]
    [InlineData(false)]
    [InlineData(true)]
    public void Model_RejectsEntitySplitting(bool explicitVersion)
    {
        using var context = explicitVersion
            ? (DbContext)new ExplicitSplitWriteDbContext(fixture.CreateOptions<ExplicitSplitWriteDbContext>("unused"))
            : new SplitWriteDbContext(fixture.CreateOptions<SplitWriteDbContext>("unused"));

        var act = () => context.Model;

        act.Should().Throw<InvalidOperationException>().WithMessage("*xmin*entity splitting*");
    }

    [Theory(DisplayName = "Automatic xmin protects domain descendants of non-domain hierarchy roots")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChanges_ProtectsDomainDescendant(bool useTpc, bool delete)
    {
        var database = PostgreSqlContainerFixture.CreateDatabaseName();
        await using var stale = CreateMixedHierarchyContext(database, useTpc);
        await stale.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
        stale.Add(new DomainDescendant { Name = "Original" });
        await stale.SaveChangesAsync(TestContext.Current.CancellationToken);
        stale.ChangeTracker.Clear();
        var entity = await stale.Set<DomainDescendant>().SingleAsync(TestContext.Current.CancellationToken);
        await using var current = CreateMixedHierarchyContext(database, useTpc);
        var changed = await current.Set<DomainDescendant>().SingleAsync(TestContext.Current.CancellationToken);
        changed.Name = "Concurrent update";
        await current.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (delete)
        {
            stale.Remove(entity);
        }
        else
        {
            entity.Name = "Stale update";
        }

        Func<Task> act = () => stale.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConcurrencyException>();
        var token = stale.Model.FindEntityType(typeof(DomainDescendant))!.GetProperty("xmin");
        token.DeclaringType.ClrType.Should().Be<NonDomainBase>();
        token.IsConcurrencyToken.Should().BeTrue();
        current.ChangeTracker.Clear();
        (await current.Set<DomainDescendant>().SingleAsync(TestContext.Current.CancellationToken)).Name
            .Should().Be("Concurrent update");
    }

    [Fact(DisplayName = "Automatic xmin protects concrete rows inherited from an abstract TPC domain entity")]
    public async Task SaveChanges_TpcHierarchyRejectsStaleDerivedUpdate()
    {
        var database = PostgreSqlContainerFixture.CreateDatabaseName();
        await using var stale = await fixture.CreateInitializedDbContextAsync<TpcWriteDbContext>(database, options => new(options));
        var entity = new ConcreteEntity { Name = "Original" };
        stale.Add(entity);
        await stale.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var current = fixture.CreateDbContext<TpcWriteDbContext>(database, options => new(options));
        var changed = await current.Set<ConcreteEntity>().SingleAsync(TestContext.Current.CancellationToken);
        changed.Name = "Concurrent update";
        await current.SaveChangesAsync(TestContext.Current.CancellationToken);
        entity.Name = "Stale update";

        Func<Task> act = () => stale.SaveChangesAsync(TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ConcurrencyException>();
        current.ChangeTracker.Clear();
        (await current.Set<ConcreteEntity>().SingleAsync(TestContext.Current.CancellationToken)).Name.Should().Be("Concurrent update");
    }

    [Theory(DisplayName = "Automatic xmin rejects TPT domain hierarchies instead of leaving derived rows unprotected")]
    [InlineData(false)]
    [InlineData(true)]
    public void Model_RejectsTptDomainHierarchy(bool explicitVersion)
    {
        using var context = explicitVersion
            ? (DbContext)new ExplicitTptWriteDbContext(fixture.CreateOptions<ExplicitTptWriteDbContext>("unused"))
            : new TptWriteDbContext(fixture.CreateOptions<TptWriteDbContext>("unused"));

        var act = () => context.Model;

        act.Should().Throw<InvalidOperationException>().WithMessage("*xmin*TPT*");
    }

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
        context.Model.FindEntityType(typeof(DerivedEntity))!.GetProperty("xmin").DeclaringType.ClrType.Should().Be<BaseEntity>();
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

        var aggregateType = context.Model.FindEntityType(typeof(TestAggregateRoot))!;
        var aggregateToken = aggregateType.FindProperty("xmin");
        var entityToken = context.Model.FindEntityType(typeof(ChildEntity))!.FindProperty("xmin");

        context.Model.FindEntityType(typeof(Entity<TestAggregateRootId>)).Should().BeNull();
        context.Model.FindEntityType(typeof(AggregateRoot<TestAggregateRootId>)).Should().BeNull();
        aggregateType.BaseType.Should().BeNull();
        aggregateType.FindDiscriminatorProperty().Should().BeNull();
        foreach (var property in new[] { aggregateToken, entityToken })
        {
            property.Should().NotBeNull();
            property!.ClrType.Should().Be<uint>();
            property.IsConcurrencyToken.Should().BeTrue();
            property.IsShadowProperty().Should().BeTrue();
            property.ValueGenerated.Should().Be(ValueGenerated.OnAddOrUpdate);
            property.GetColumnName().Should().Be("xmin");
        }
        context.Model.FindEntityType(typeof(NonAuditableEntity))!.FindProperty("xmin").Should().BeNull();
        context.Database.GenerateCreateScript().Should().NotContain("\"xmin\"");
    }

    [Fact(DisplayName = "Repository updates a tracked aggregate and refreshes its shadow xmin")]
    public async Task SaveChanges_RefreshesShadowXmin()
    {
        await using var context = await CreateContextAsync();
        var repository = new XminRepository(context);
        var entity = new TestAggregateRoot(TestAggregateRootId.New()) { Name = "Original" };
        repository.Add(entity);
        await new EfUnitOfWork<XminWriteDbContext>(context).SaveChangesAsync(TestContext.Current.CancellationToken);
        context.ChangeTracker.Clear();
        var loaded = (await repository.GetByIdAsync(entity.Id, TestContext.Current.CancellationToken))!;
        var originalVersion = context.Entry(loaded).Property<uint>("xmin").OriginalValue;

        loaded.Name = "Updated";
        repository.Update(loaded);
        await new EfUnitOfWork<XminWriteDbContext>(context).SaveChangesAsync(TestContext.Current.CancellationToken);

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
        var repository = new XminRepository(stale);
        var loaded = (await repository.GetByIdAsync(entity.Id, TestContext.Current.CancellationToken))!;
        await using var current = fixture.CreateDbContext<XminWriteDbContext>(database, options => new(options));
        var changed = await current.Set<TestAggregateRoot>().SingleAsync(TestContext.Current.CancellationToken);
        changed.Name = "Concurrent update";
        await current.SaveChangesAsync(TestContext.Current.CancellationToken);
        loaded.Name = "Stale update";

        if (delete)
        {
            repository.Delete(loaded);
        }
        else
        {
            repository.Update(loaded);
        }
        Func<Task> act = () => new EfUnitOfWork<XminWriteDbContext>(stale).SaveChangesAsync(TestContext.Current.CancellationToken);

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

    private DbContext CreateMixedHierarchyContext(string database, bool useTpc)
    {
        return useTpc
            ? fixture.CreateDbContext<MixedTpcWriteDbContext>(database, options => new(options))
            : fixture.CreateDbContext<MixedTphWriteDbContext>(database, options => new(options));
    }

    private static void ConfigureSplitEntity(ModelBuilder modelBuilder, bool explicitVersion)
    {
        var builder = modelBuilder.Entity<SplitEntity>().ToTable("split_entities");
        builder.SplitToTable("split_details", table =>
        {
            table.Property(entity => entity.Id);
            table.Property(entity => entity.Details);
        });
        if (explicitVersion)
        {
            builder.Property<uint>("Version").IsRowVersion();
        }
    }

    private sealed class SplitEntity : IEntity
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    private sealed class SplitWriteDbContext(DbContextOptions<SplitWriteDbContext> options)
        : WriteDbContext<SplitWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureSplitEntity(modelBuilder, false);
        }
    }

    private sealed class ExplicitSplitWriteDbContext(DbContextOptions<ExplicitSplitWriteDbContext> options)
        : WriteDbContext<ExplicitSplitWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            ConfigureSplitEntity(modelBuilder, true);
        }
    }

    private abstract class NonDomainBase
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private sealed class DomainDescendant : NonDomainBase, IEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class MixedTphWriteDbContext(DbContextOptions<MixedTphWriteDbContext> options)
        : WriteDbContext<MixedTphWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NonDomainBase>().UseTphMappingStrategy();
            modelBuilder.Entity<DomainDescendant>();
        }
    }

    private sealed class MixedTpcWriteDbContext(DbContextOptions<MixedTpcWriteDbContext> options)
        : WriteDbContext<MixedTpcWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<NonDomainBase>().UseTpcMappingStrategy();
            modelBuilder.Entity<DomainDescendant>();
        }
    }

    private sealed class XminRepository(XminWriteDbContext context)
        : EfRepository<XminWriteDbContext, TestAggregateRootId, TestAggregateRoot>(context);

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

    private abstract class AbstractEntity : IEntity
    {
        public Guid Id { get; set; } = Guid.NewGuid();
    }

    private sealed class ConcreteEntity : AbstractEntity
    {
        public string Name { get; set; } = string.Empty;
    }

    private sealed class TpcWriteDbContext(DbContextOptions<TpcWriteDbContext> options)
        : WriteDbContext<TpcWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AbstractEntity>().UseTpcMappingStrategy();
            modelBuilder.Entity<ConcreteEntity>();
        }
    }

    private sealed class TptWriteDbContext(DbContextOptions<TptWriteDbContext> options)
        : WriteDbContext<TptWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BaseEntity>().ToTable("base_entities");
            modelBuilder.Entity<DerivedEntity>().ToTable("derived_entities");
        }
    }

    private sealed class ExplicitTptWriteDbContext(DbContextOptions<ExplicitTptWriteDbContext> options)
        : WriteDbContext<ExplicitTptWriteDbContext>(options, [])
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<BaseEntity>().UseTptMappingStrategy().Property<uint>("Version").IsRowVersion();
            modelBuilder.Entity<DerivedEntity>();
        }
    }

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
