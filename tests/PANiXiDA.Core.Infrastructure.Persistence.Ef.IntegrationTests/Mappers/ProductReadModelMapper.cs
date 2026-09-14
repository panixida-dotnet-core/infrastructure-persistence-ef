using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Mappers;

internal sealed partial class ProductReadModelMapper
    : IReadModelMapper<int, ProductReadDbModel, ProductReadModel>
{
    public static IQueryable<ProductReadModel> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new ProductReadModel { Id = item.Id, Name = item.Name, Score = item.Score });
    }
}
