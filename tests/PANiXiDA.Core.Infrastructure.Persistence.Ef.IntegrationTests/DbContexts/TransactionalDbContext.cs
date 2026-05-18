using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class TransactionalDbContext(DbContextOptions<TransactionalDbContext> options)
    : DbContext(options)
{
    public DbSet<TransactionalEntity> Entities => Set<TransactionalEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionalEntity>(builder =>
        {
            builder.HasKey(item => item.Id);
        });
    }
}
