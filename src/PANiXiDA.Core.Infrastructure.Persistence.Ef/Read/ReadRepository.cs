using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Application.Querying.Cursor;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

using System.Linq.Dynamic.Core;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

public abstract class ReadRepository
    <TDbContext, TId, TReadDbModel>(TDbContext dbContext) : IReadRepository<TId>
    where TDbContext : ReadDbContext<TDbContext>
    where TId : struct
    where TReadDbModel : ReadDbModel<TId>
{
    protected readonly IQueryable<TReadDbModel> Query = dbContext.Set<TReadDbModel>().AsNoTracking();

    public virtual Task<bool> ExistsByIdAsync(TId id, CancellationToken cancellationToken)
    {
        return Query.AnyAsync(item => item.Id.Equals(id), cancellationToken);
    }

    public virtual Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return Query.AnyAsync(cancellationToken);
    }

    protected virtual Task<TReadModel?> GetByIdAsync<TReadModel, TReadModelMapper>(
        TId id,
        CancellationToken cancellationToken)
        where TReadModelMapper : IReadModelMapper<TId, TReadDbModel, TReadModel>
    {
        var query = Query.Where(item => item.Id.Equals(id));
        var dtoQuery = TReadModelMapper.ProjectTo(query);
        return dtoQuery.FirstOrDefaultAsync(cancellationToken);
    }

    protected virtual async Task<PaginationResult<TReadModel>> GetPagedResultAsync<TReadModel, TReadModelMapper>(
        IQueryable<TReadDbModel> query,
        PaginationParameters paginationParameters,
        SortParameters sortParameters,
        CancellationToken cancellationToken)
        where TReadModelMapper : IReadModelMapper<TId, TReadDbModel, TReadModel>
    {
        var totalCount = await query.LongCountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return PaginationResult<TReadModel>.Empty(
                paginationParameters.PageNumber,
                paginationParameters.PageSize);
        }

        query = ApplySort(query, sortParameters);
        query = ApplyPagination(query, paginationParameters);

        var dtoQuery = TReadModelMapper.ProjectTo(query);

        var items = await dtoQuery.ToListAsync(cancellationToken);

        return PaginationResult<TReadModel>.Create(
            items,
            paginationParameters.PageNumber,
            paginationParameters.PageSize,
            totalCount);
    }

    protected virtual IQueryable<TReadDbModel> ApplyPagination(
        IQueryable<TReadDbModel> query,
        PaginationParameters paginationParameters)
    {
        return query
            .Skip(paginationParameters.Skip)
            .Take(paginationParameters.Take);
    }

    protected virtual IQueryable<TReadDbModel> ApplySort(
        IQueryable<TReadDbModel> query,
        SortParameters sortParameters)
    {
        var id = nameof(ReadDbModel<>.Id);

        if (string.IsNullOrWhiteSpace(sortParameters.Field))
        {
            return query.OrderBy($"{id} descending");
        }

        var sortField = sortParameters.Field.Trim();

        var sortDirection = sortParameters.Order == SortOrder.Descending
            ? "descending"
            : "ascending";

        if (string.Equals(sortField, id, StringComparison.OrdinalIgnoreCase))
        {
            return query.OrderBy($"{id} {sortDirection}");
        }

        return query.OrderBy($"{sortField} {sortDirection}, {id} descending");
    }

    /// <summary>
    /// Возвращает результат курсорной пагинации.
    /// </summary>
    /// <remarks>
    /// В query уже должны быть применены:
    /// 1. стабильная сортировка;
    /// 2. фильтр относительно курсора, если курсор передан.
    /// </remarks>
    protected static async Task<CursorPaginationResult<TReadDbModel>> GetCursorResultAsync(
        IQueryable<TReadDbModel> query,
        CursorPaginationParameters paginationParameters,
        Func<TReadDbModel, string> cursorFactory,
        CancellationToken cancellationToken)
    {
        var limit = Math.Max(paginationParameters.Limit, 1);

        var loadedItems = await query
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasExtraItem = loadedItems.Count > limit;
        if (hasExtraItem)
        {
            loadedItems.RemoveAt(limit);
        }

        if (paginationParameters.Direction == CursorDirection.Backward)
        {
            loadedItems.Reverse();
        }

        if (loadedItems.Count == 0)
        {
            return CursorPaginationResult<TReadDbModel>.Empty(limit);
        }

        var hasIncomingCursor = !string.IsNullOrWhiteSpace(paginationParameters.Cursor);

        var hasNextPage = paginationParameters.Direction == CursorDirection.Forward
            ? hasExtraItem
            : hasIncomingCursor;

        var hasPreviousPage = paginationParameters.Direction == CursorDirection.Forward
            ? hasIncomingCursor
            : hasExtraItem;

        var firstItem = loadedItems[0];
        var lastItem = loadedItems[^1];

        return CursorPaginationResult<TReadDbModel>.Create(
            loadedItems,
            limit,
            hasNextPage ? cursorFactory(lastItem) : null,
            hasPreviousPage ? cursorFactory(firstItem) : null,
            hasNextPage,
            hasPreviousPage);
    }
}
