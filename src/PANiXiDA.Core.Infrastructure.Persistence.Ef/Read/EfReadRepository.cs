using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Application.Querying.Cursor;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Mapping;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

/// <summary>
/// Provides a base EF Core read repository for database read models.
/// </summary>
/// <typeparam name="TDbContext">The read DbContext type.</typeparam>
/// <typeparam name="TId">The read model identifier type.</typeparam>
/// <typeparam name="TReadDbModel">The database read model type.</typeparam>
/// <param name="dbContext">The read DbContext used by the repository.</param>
[SuppressMessage("Design", "S2436", Justification = "The existing repository contract independently specifies the DbContext, identifier and read model types.")]
public abstract class EfReadRepository
    <TDbContext, TId,
        [DynamicallyAccessedMembers(TrimmingConstants.EntityMembers)] TReadDbModel>(TDbContext dbContext) : IReadRepository<TId>
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
    /// <typeparam name="TReadModelSorting">The sorting implementation and defaults for the projected model.</typeparam>
    /// <param name="query">The query to paginate.</param>
    /// <param name="paginationParameters">The page-based pagination parameters.</param>
    /// <param name="sortingParameters">The sorting parameters.</param>
    /// <param name="cancellationToken">The token used to cancel the operation.</param>
    /// <returns>The paged projected read model result.</returns>
    protected virtual async Task<PaginationResult<TReadModel>> GetPagedResultAsync<TReadModel, TReadModelMapper, TReadModelSorting>(
        IQueryable<TReadDbModel> query,
        PaginationParameters paginationParameters,
        SortingParameters sortingParameters,
        CancellationToken cancellationToken)
        where TReadModelMapper : IReadModelMapper<TId, TReadDbModel, TReadModel>
        where TReadModelSorting : IReadModelSorting<TReadModel>
    {
        var dtoQuery = TReadModelMapper.ProjectTo(query);
        dtoQuery = TReadModelSorting.ApplySorting(dtoQuery, sortingParameters);
        var totalCount = await dtoQuery.LongCountAsync(cancellationToken);

        if (totalCount == 0)
        {
            return PaginationResult<TReadModel>.Empty(
                paginationParameters.PageNumber,
                paginationParameters.PageSize);
        }

        dtoQuery = ApplyPagination(dtoQuery, paginationParameters);

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
    /// <typeparam name="TReadModel">The projected read model type.</typeparam>
    /// <param name="query">The query to paginate.</param>
    /// <param name="paginationParameters">The page-based pagination parameters.</param>
    /// <returns>The paginated query.</returns>
    protected virtual IQueryable<TReadModel> ApplyPagination<TReadModel>(
        IQueryable<TReadModel> query,
        PaginationParameters paginationParameters)
    {
        return query
            .Skip(paginationParameters.Skip)
            .Take(paginationParameters.Take);
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
