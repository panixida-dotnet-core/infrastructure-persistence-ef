namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

internal sealed class ModelBuilderOwner
{
    public int Id { get; set; }
    public ModelBuilderOwned Owned { get; set; } = new();
    public List<ModelBuilderOwnedItem> OwnedItems { get; set; } = [];
    public List<ModelBuilderLegacyOwnedItem> LegacyOwnedItems { get; set; } = [];
}
