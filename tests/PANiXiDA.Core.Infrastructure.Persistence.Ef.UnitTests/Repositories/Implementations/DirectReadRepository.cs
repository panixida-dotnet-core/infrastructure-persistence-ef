using PANiXiDA.Core.Application.Persistence;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal sealed class DirectReadRepository : IReadRepository<int>
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
