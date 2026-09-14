using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

using PANiXiDA.Core.Application.Querying.Sorting;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

/// <summary>
/// Defines a static projection from a database read model query to an application read model query.
/// </summary>
/// <typeparam name="TId">The database read model identifier type.</typeparam>
/// <typeparam name="TDbReadModel">The database read model type.</typeparam>
/// <typeparam name="TReadModel">The projected read model type.</typeparam>
public interface IReadModelMapper<TId, TDbReadModel, TReadModel>
    where TId : struct
    where TDbReadModel : ReadDbModel<TId>
{
    /// <summary>
    /// Projects the specified database read model query to the application read model query.
    /// </summary>
    /// <param name="query">The database read model query to project.</param>
    /// <returns>The projected read model query.</returns>
    static abstract IQueryable<TReadModel> ProjectTo(IQueryable<TDbReadModel> query);

    /// <summary>
    /// Applies sorting to the projected read model query using generated property selectors.
    /// </summary>
    /// <param name="query">The projected query to sort.</param>
    /// <param name="sortingParameters">Validated sorting criteria in their order of precedence.</param>
    /// <returns>The sorted query, or the original query when no criteria are supplied.</returns>
    static abstract IQueryable<TReadModel> ApplySorting(IQueryable<TReadModel> query, SortingParameters sortingParameters);
}
