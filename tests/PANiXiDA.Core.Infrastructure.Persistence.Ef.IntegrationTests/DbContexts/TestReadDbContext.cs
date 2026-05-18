using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class TestReadDbContext(
    DbContextOptions<TestReadDbContext> options)
    : ReadDbContext<TestReadDbContext>(options)
{
}
