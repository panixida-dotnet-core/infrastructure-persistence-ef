using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Implementations;

internal sealed class AssemblyReadRepository : IAssemblyReadRepository
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
