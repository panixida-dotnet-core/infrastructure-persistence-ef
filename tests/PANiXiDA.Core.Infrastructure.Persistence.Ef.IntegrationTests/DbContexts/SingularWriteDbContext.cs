using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;

internal sealed class SingularWriteDbContext(
    DbContextOptions<SingularWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<SingularWriteDbContext>(options, interceptors)
{
    protected override bool UsePluralTableNames => false;
}
