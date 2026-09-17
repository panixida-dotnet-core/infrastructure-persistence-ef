using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Sorting;

internal sealed partial class ProductReadModelSorting : IReadModelSorting<ProductReadModel>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
}
