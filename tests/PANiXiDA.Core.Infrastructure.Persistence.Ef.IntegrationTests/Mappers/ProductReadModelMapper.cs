using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Mappers;

internal sealed class ProductReadModelMapper
    : IReadModelMapper<int, ProductReadDbModel, ProductReadModel>
{
    public static IQueryable<ProductReadModel> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new ProductReadModel(item.Id, item.Name, item.Score));
    }
}
