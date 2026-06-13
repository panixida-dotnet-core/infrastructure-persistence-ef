using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal sealed class ReadRepository : IReadRepositoryContract
{
    public Task<bool> ExistsByIdAsync(
        int id,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }

    public Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
