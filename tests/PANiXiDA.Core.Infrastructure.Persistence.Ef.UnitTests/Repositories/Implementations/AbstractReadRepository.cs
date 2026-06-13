using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal abstract class AbstractReadRepository : IAbstractReadRepositoryContract
{
    public abstract Task<bool> ExistsByIdAsync(
        int id,
        CancellationToken cancellationToken);

    public abstract Task<bool> AnyAsync(CancellationToken cancellationToken);
}
