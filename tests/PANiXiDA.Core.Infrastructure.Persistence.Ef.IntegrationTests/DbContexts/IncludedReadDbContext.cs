using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class IncludedReadDbContext(
    DbContextOptions<IncludedReadDbContext> options)
    : ReadDbContext<IncludedReadDbContext>(options)
{
    protected override bool ExcludeReadModelsFromMigrations => false;
}
