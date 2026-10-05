using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class AuditMetadataCacheTests
{
    [Fact(DisplayName = "Audit metadata is reused across contexts sharing the same EF model")]
    public void GetAuditProperties_ReusesCachedMetadata()
    {
        using var firstContext = new FirstAuditCacheDbContext();
        using var secondContext = new FirstAuditCacheDbContext();
        var firstEntityType = firstContext.Model.FindEntityType(typeof(AuditCacheEntity))!;
        var secondEntityType = secondContext.Model.FindEntityType(typeof(AuditCacheEntity))!;

        var first = AuditSaveChangesInterceptor.GetAuditProperties(firstEntityType);
        var second = AuditSaveChangesInterceptor.GetAuditProperties(secondEntityType);

        firstEntityType.Should().BeSameAs(secondEntityType);
        second.Should().BeSameAs(first);
        first.CreatedAt!.Name.Should().Be(nameof(AuditCacheEntity.Creation));
        first.UpdatedAt.Should().BeNull();
        first.DeletedAt.Should().BeNull();
    }

    [Fact(DisplayName = "Audit metadata stays isolated for the same CLR entity in different EF models")]
    public void GetAuditProperties_IsolatesDifferentModels()
    {
        using var firstContext = new FirstAuditCacheDbContext();
        using var secondContext = new SecondAuditCacheDbContext();
        var firstEntityType = firstContext.Model.FindEntityType(typeof(AuditCacheEntity))!;
        var secondEntityType = secondContext.Model.FindEntityType(typeof(AuditCacheEntity))!;

        var first = AuditSaveChangesInterceptor.GetAuditProperties(firstEntityType);
        var second = AuditSaveChangesInterceptor.GetAuditProperties(secondEntityType);

        second.Should().NotBeSameAs(first);
        first.CreatedAt!.Name.Should().Be(nameof(AuditCacheEntity.Creation));
        second.CreatedAt!.Name.Should().Be(nameof(AuditCacheEntity.AlternateCreation));
        second.CreatedAt.DeclaringType.Should().BeSameAs(secondEntityType);
    }

    [Fact(DisplayName = "Audit column discovery does not assign table properties to a view-only entity")]
    public void GetAuditProperties_DoesNotMatchColumnsWithoutTableMapping()
    {
        using var context = new ViewAuditCacheDbContext();
        var entityType = context.Model.FindEntityType(typeof(AuditCacheEntity))!;

        var properties = AuditSaveChangesInterceptor.GetAuditProperties(entityType);

        entityType.GetTableName().Should().BeNull();
        properties.CreatedAt.Should().BeNull();
        properties.UpdatedAt.Should().BeNull();
        properties.DeletedAt.Should().BeNull();
    }

    [Fact(DisplayName = "Unsupported audit CLR types without converters fail instead of being silently ignored")]
    public void SavingChanges_RejectsUnsupportedAuditTypeWithoutConverter()
    {
        using var context = new UnsupportedAuditDbContext();
        var entity = new UnsupportedAuditEntity { Id = 1 };
        context.Add(entity);
        var interceptor = new AuditSaveChangesInterceptor(TimeProvider.System);
        var eventData = new DbContextEventData(null!, static (_, _) => string.Empty, context);

        var act = () => interceptor.SavingChanges(eventData, default);

        act.Should().Throw<InvalidCastException>();
    }

    [Fact(DisplayName = "An audit override without DeletedAt retains the query translation failure for soft delete")]
    public void ConfigureSoftDelete_ReportsMissingPropertyDuringQueryTranslation()
    {
        using var context = new MissingAuditDbContext();

        var act = () => context.Set<UnsupportedAuditEntity>().ToQueryString();

        act.Should().Throw<InvalidOperationException>().WithMessage("*EF.Property*DeletedAt*failed*");
    }

    [Theory(DisplayName = "Audit timestamps retain CLR DateTime values when database converters store ticks")]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void SavingChanges_SupportsDateTimePropertiesWithStorageConverters(EntityState state)
    {
        using var context = new TicksAuditDbContext();
        var now = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var initial = now.AddDays(-1).UtcDateTime;
        var entity = new TicksAuditEntity { Id = 1, CreatedAt = initial, UpdatedAt = initial };
        context.Entry(entity).State = state;
        var interceptor = new AuditSaveChangesInterceptor(new AuditCacheTimeProvider(now));
        var eventData = new DbContextEventData(null!, static (_, _) => string.Empty, context);

        interceptor.SavingChanges(eventData, default);

        entity.CreatedAt.Should().Be(state == EntityState.Added ? now.UtcDateTime : initial);
        entity.UpdatedAt.Should().Be(now.UtcDateTime);
        entity.DeletedAt.Should().Be(state == EntityState.Deleted ? now.UtcDateTime : null);
    }
}

internal sealed class AuditCacheEntity
{
    public int Id { get; set; }

    public DateTime Creation { get; set; }

    public DateTime AlternateCreation { get; set; }
}

internal abstract class AuditCacheDbContext : DbContext
{
    internal const string ConnectionString = "Host=localhost;Database=audit_cache_tests;Username=postgres";

    protected abstract bool UseAlternateCreation { get; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<AuditCacheEntity>();
        if (UseAlternateCreation)
        {
            entity.Ignore(item => item.Creation);
            entity.Property(item => item.AlternateCreation).HasColumnName(EfConstants.CreatedAt);
        }
        else
        {
            entity.Ignore(item => item.AlternateCreation);
            entity.Property(item => item.Creation).HasColumnName(EfConstants.CreatedAt);
        }
    }
}

internal sealed class FirstAuditCacheDbContext : AuditCacheDbContext
{
    protected override bool UseAlternateCreation => false;
}

internal sealed class SecondAuditCacheDbContext : AuditCacheDbContext
{
    protected override bool UseAlternateCreation => true;
}

internal sealed class ViewAuditCacheDbContext : AuditCacheDbContext
{
    protected override bool UseAlternateCreation => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<AuditCacheEntity>().ToView("audit_view").ToTable((string?)null);
    }
}

internal sealed class UnsupportedAuditEntity
{
    public int Id { get; set; }

    public string CreatedAt { get; set; } = string.Empty;
}

internal sealed class UnsupportedAuditDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(AuditCacheDbContext.ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<UnsupportedAuditEntity>();
    }
}

internal sealed class MissingAuditDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(AuditCacheDbContext.ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfiguration(new MissingAuditConfiguration(true));
    }
}

internal sealed class MissingAuditConfiguration(bool softDeleteEnabled) : AuditableEntityConfiguration<UnsupportedAuditEntity>
{
    protected override void ConfigureEntity(EntityTypeBuilder<UnsupportedAuditEntity> builder)
    {
        builder.HasKey(item => item.Id);
    }

    protected override void ConfigureAudit(EntityTypeBuilder<UnsupportedAuditEntity> builder)
    {
    }

    protected override bool IsSoftDeleteEnabled() => softDeleteEnabled;
}

internal sealed class TicksAuditEntity
{
    public int Id { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}

internal sealed class TicksAuditDbContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql(AuditCacheDbContext.ConnectionString);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TicksAuditEntity>();
        entity.Property(item => item.CreatedAt).HasConversion<long>();
        entity.Property(item => item.UpdatedAt).HasConversion<long>();
        entity.Property(item => item.DeletedAt).HasConversion<long>();
    }
}

internal sealed class AuditCacheTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
