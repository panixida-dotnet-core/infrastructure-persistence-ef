namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

internal sealed class ModelBuilderOwner
{
    public int Id { get; set; }
    public ModelBuilderOwned Owned { get; set; } = new();
}
