using PANiXiDA.Core.Application.Querying.Cursor;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Mappers;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;

internal sealed class ExposedReadRepository(WritableReadDbContext dbContext)
    : EfReadRepository<WritableReadDbContext, int, ProductReadDbModel>(dbContext)
{
    public Task<ProductReadModel?> GetProductByIdAsync(int id)
    {
        return GetByIdAsync<ProductReadModel, ProductReadModelMapper>(
            id,
            TestContext.Current.CancellationToken);
    }

    public Task<PaginationResult<ProductReadModel>> GetProductsPageAsync(
        IQueryable<ProductReadDbModel> query,
        PaginationParameters paginationParameters,
        SortParameters sortParameters)
    {
        return GetPagedResultAsync<ProductReadModel, ProductReadModelMapper>(
            query,
            paginationParameters,
            sortParameters,
            TestContext.Current.CancellationToken);
    }

    public IQueryable<ProductReadDbModel> ApplyPaginationForTest(
        IQueryable<ProductReadDbModel> query,
        PaginationParameters paginationParameters)
    {
        return ApplyPagination(query, paginationParameters);
    }

    public IQueryable<ProductReadDbModel> ApplySortForTest(
        IQueryable<ProductReadDbModel> query,
        SortParameters sortParameters)
    {
        return ApplySort(query, sortParameters);
    }

    public static Task<CursorPaginationResult<ProductReadDbModel>> GetCursorPageAsync(
        IQueryable<ProductReadDbModel> query,
        CursorPaginationParameters paginationParameters,
        Func<ProductReadDbModel, string> cursorFactory)
    {
        return GetCursorResultAsync(
            query,
            paginationParameters,
            cursorFactory,
            TestContext.Current.CancellationToken);
    }

    public IQueryable<ProductReadDbModel> Products => Query;
}
