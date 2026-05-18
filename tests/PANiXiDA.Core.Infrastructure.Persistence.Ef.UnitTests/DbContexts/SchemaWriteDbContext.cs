using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.DbContexts;

internal abstract class SchemaWriteDbContext(
    DbContextOptions<SchemaWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<SchemaWriteDbContext>(options, interceptors)
{
}
