using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;

internal sealed class ExposedWriteRepository(TestWriteDbContext dbContext)
    : EfRepository<TestWriteDbContext, TestAggregateRootId, TestAggregateRoot>(dbContext)
{
    public IQueryable<TestAggregateRoot> QueryForTest => Query;
    public DbSet<TestAggregateRoot> DbSetForTest => DbSet;
}
