using PANiXiDA.Core.Application.Querying;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;

internal sealed record ProductReadModel : IReadModel
{
    public int Id { get; init; }
    public required string Name { get; init; }
    public int Score { get; init; }
}
