using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;

internal abstract class WriteDbContextAccessor
{
    public abstract DbContext Context { get; }
}

internal sealed class WriteDbContextAccessor<TDbContext>(TDbContext context) : WriteDbContextAccessor
    where TDbContext : DbContext
{
    public override DbContext Context { get; } = context;
}
