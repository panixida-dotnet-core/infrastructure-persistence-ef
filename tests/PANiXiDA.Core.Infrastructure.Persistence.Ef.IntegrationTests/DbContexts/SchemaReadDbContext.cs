using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class SchemaReadDbContext(
    DbContextOptions<SchemaReadDbContext> options)
    : ReadDbContext<SchemaReadDbContext>(options)
{
    protected override bool UseContextNameAsSchema => true;
}
