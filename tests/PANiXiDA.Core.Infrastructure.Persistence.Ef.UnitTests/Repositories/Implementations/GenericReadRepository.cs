using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Implementations;

internal sealed class GenericReadRepository<T>(T value) : IGenericReadRepositoryContract
{
    public T Value { get; } = value;

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
