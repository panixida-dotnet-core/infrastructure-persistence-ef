using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Application.Querying.Cursor;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

using System.Linq.Dynamic.Core;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

/// <summary>
/// Provides a base EF Core read repository for database read models.
/// </summary>
/// <typeparam name="TDbContext">The read DbContext type.</typeparam>
/// <typeparam name="TId">The read model identifier type.</typeparam>
/// <typeparam name="TReadDbModel">The database read model type.</typeparam>
/// <param name="dbContext">The read DbContext used by the repository.</param>
public abstract class EfReadRepository
    <TDbContext, TId, TReadDbModel>(TDbContext dbContext) : IReadRepository<TId>
    where TDbContext : ReadDbContext<TDbContext>
    where TId : struct
    where TReadDbModel : ReadDbModel<TId>
{
    /// <summary>
    /// Gets the base no-tracking query used by read operations.
    /// </summary>
    protected readonly IQueryable<TReadDbModel> Query = dbContext.Set<TReadDbModel>().AsNoTracking();

    /// <inheritdoc />
    public virtual Task<bool> ExistsByIdAsync(TId id, CancellationToken cancellationToken)
    {
        return Query.AnyAsync(item => item.Id.Equals(id), cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(CancellationToken cancellationToken)
    {
        return Query.AnyAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a projected read model by database read model identifier.
    /// </summary>
    /// <typeparam name="TReadModel">The projected read model type.</typeparam>
    /// <typeparam name="TReadModelMapper">The mapper used to project the database read model.</typeparam>
    /// <param name="id">The database read model identifier.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The projected read model when found; otherwise, <see langword="null"/>.</returns>
    protected virtual Task<TReadModel?> GetByIdAsync<TReadModel, TReadModelMapper>(
        TId id,
        CancellationToken cancellationToken)
        where TReadModelMapper : IReadModelMapper<TId, TReadDbModel, TReadModel>
    {
        var query = Query.Where(item => item.Id.Equals(id));
        var dtoQuery = TReadModelMapper.ProjectTo(query);
        return dtoQuery.FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>
    /// Gets a projected page of read models.
    /// </summary>
    /// <typeparam name="TReadModel">The projected read model type.</typeparam>
    /// <typeparam name="TReadModelMapper">The mapper used to project database read models.</typeparam>
    /// <param name="query">The query to paginate.</param>
    /// <param name="paginationParameters">The page-based pagination parameters.</param>
    /// <param name="sortParameters">The sorting parameters.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The paged projected read model result.</returns>
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

    /// <summary>
    /// Applies page-based pagination to the specified query.
    /// </summary>
    /// <param name="query">The query to paginate.</param>
    /// <param name="paginationParameters">The page-based pagination parameters.</param>
    /// <returns>The paginated query.</returns>
    protected virtual IQueryable<TReadDbModel> ApplyPagination(
        IQueryable<TReadDbModel> query,
        PaginationParameters paginationParameters)
    {
        return query
            .Skip(paginationParameters.Skip)
            .Take(paginationParameters.Take);
    }

    /// <summary>
    /// Applies sorting to the specified query and falls back to descending identifier sorting when no field is provided.
    /// </summary>
    /// <param name="query">The query to sort.</param>
    /// <param name="sortParameters">The sorting parameters.</param>
    /// <returns>The sorted query.</returns>
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
    /// Gets a cursor-based pagination result from a prepared query.
    /// </summary>
    /// <remarks>
    /// The query must already include stable sorting and cursor-boundary filtering when a cursor is provided.
    /// </remarks>
    /// <param name="query">The prepared query to paginate.</param>
    /// <param name="paginationParameters">The cursor-based pagination parameters.</param>
    /// <param name="cursorFactory">The function that creates a cursor from a page boundary item.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The cursor-based pagination result.</returns>
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
