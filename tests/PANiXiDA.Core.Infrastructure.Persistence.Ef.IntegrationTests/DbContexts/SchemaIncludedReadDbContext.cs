using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class SchemaIncludedReadDbContext(
    DbContextOptions<SchemaIncludedReadDbContext> options)
    : ReadDbContext<SchemaIncludedReadDbContext>(options)
{
    protected override bool UseContextNameAsSchema => true;
    protected override bool ExcludeReadModelsFromMigrations => false;
}
