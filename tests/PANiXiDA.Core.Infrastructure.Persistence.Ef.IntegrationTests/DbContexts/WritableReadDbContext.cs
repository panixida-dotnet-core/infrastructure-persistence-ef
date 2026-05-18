using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class WritableReadDbContext(
    DbContextOptions<WritableReadDbContext> options)
    : ReadDbContext<WritableReadDbContext>(options)
{
    protected override bool ExcludeReadModelsFromMigrations => false;
}
