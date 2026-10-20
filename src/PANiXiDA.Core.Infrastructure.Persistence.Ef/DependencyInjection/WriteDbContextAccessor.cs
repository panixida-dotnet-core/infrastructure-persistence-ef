using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;

internal interface IWriteDbContextAccessor
{
    DbContext Context { get; }
}

internal sealed class WriteDbContextAccessor<TDbContext>(TDbContext context) : IWriteDbContextAccessor
    where TDbContext : DbContext
{
    public DbContext Context { get; } = context;
}
