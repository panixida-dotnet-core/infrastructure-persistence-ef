using PANiXiDA.Core.Application.Querying.Sorting;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting;

/// <summary>
/// Defines default criteria and typed sorting for a projected read model.
/// </summary>
/// <typeparam name="TReadModel">The projected read model type.</typeparam>
public interface IReadModelSorting<TReadModel>
{
    /// <summary>
    /// Gets default criteria appended after client criteria when their fields are not already present.
    /// Use <see cref="SortingParameters.None"/> to explicitly omit default sorting.
    /// </summary>
    static abstract SortingParameters DefaultSorting { get; }

    /// <summary>
    /// Applies client sorting followed by defaults for fields not specified by the client.
    /// </summary>
    /// <param name="query">The projected query to sort.</param>
    /// <param name="sortingParameters">Validated client criteria in their order of precedence.</param>
    /// <returns>The sorted query, or the original query when both client and default criteria are empty.</returns>
    static abstract IQueryable<TReadModel> ApplySorting(IQueryable<TReadModel> query, SortingParameters sortingParameters);
}
