using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class TestWriteDbContext(
    DbContextOptions<TestWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<TestWriteDbContext>(options, interceptors)
{
    public DbSet<TestAggregateRoot> Aggregates => Set<TestAggregateRoot>();
    public DbSet<NoSoftDeleteAggregateRoot> NoSoftDeleteAggregates => Set<NoSoftDeleteAggregateRoot>();
    public DbSet<NonAuditableEntity> NonAuditableEntities => Set<NonAuditableEntity>();
    public DbSet<UpdatedOnlyEntity> UpdatedOnlyEntities => Set<UpdatedOnlyEntity>();
}
