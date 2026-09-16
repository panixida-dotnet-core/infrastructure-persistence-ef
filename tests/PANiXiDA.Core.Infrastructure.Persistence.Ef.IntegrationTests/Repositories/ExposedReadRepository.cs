using PANiXiDA.Core.Application.Querying.Cursor;
using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Mappers;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Sorting;

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
        SortingParameters sortingParameters)
    {
        return GetPagedResultAsync<ProductReadModel, ProductReadModelMapper, ProductReadModelSorting>(
            query,
            paginationParameters,
            sortingParameters,
            TestContext.Current.CancellationToken);
    }

    public IQueryable<ProductReadDbModel> ApplyPaginationForTest(
        IQueryable<ProductReadDbModel> query,
        PaginationParameters paginationParameters)
    {
        return ApplyPagination(query, paginationParameters);
    }

    public static IQueryable<ProductReadModel> ApplySortForTest(
        IQueryable<ProductReadDbModel> query,
        SortingParameters sortingParameters)
    {
        return ProductReadModelSorting.ApplySorting(ProductReadModelMapper.ProjectTo(query), sortingParameters);
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

    public Task<PaginationResult<TReadModel>> GetProjectionPageAsync<TReadModel, TMapper, TSorting>(
        PaginationParameters pagination, SortingParameters sortingParameters)
        where TMapper : IReadModelMapper<int, ProductReadDbModel, TReadModel>
        where TSorting : IReadModelSorting<TReadModel>
    {
        return GetPagedResultAsync<TReadModel, TMapper, TSorting>(Query, pagination, sortingParameters, TestContext.Current.CancellationToken);
    }

    public Task<List<TReadModel>> GetProjectionListAsync<TReadModel, TMapper, TSorting>(SortingParameters sortingParameters)
        where TMapper : IReadModelMapper<int, ProductReadDbModel, TReadModel>
        where TSorting : IReadModelSorting<TReadModel>
    {
        var query = TMapper.ProjectTo(Query);
        query = TSorting.ApplySorting(query, sortingParameters);
        return query.ToListAsync(TestContext.Current.CancellationToken);
    }
}
