using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Interceptors;

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
    protected abstract bool UseAlternateCreation { get; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        optionsBuilder.UseNpgsql("Host=localhost;Database=audit_cache_tests;Username=postgres");
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
        optionsBuilder.UseNpgsql("Host=localhost;Database=audit_cache_tests;Username=postgres");
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
