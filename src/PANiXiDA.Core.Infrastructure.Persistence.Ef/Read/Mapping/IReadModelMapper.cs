using PANiXiDA.Core.Application.Querying;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Mapping;

/// <summary>
/// Defines a static projection from a database read model query to an application read model query.
/// </summary>
/// <typeparam name="TId">The database read model identifier type.</typeparam>
/// <typeparam name="TDbReadModel">The database read model type.</typeparam>
/// <typeparam name="TReadModel">The projected application model implementing <see cref="IReadModel"/>.</typeparam>
public interface IReadModelMapper<TId, TDbReadModel, TReadModel>
    where TId : struct
    where TDbReadModel : ReadDbModel<TId>
    where TReadModel : IReadModel
{
    /// <summary>
    /// Projects the specified database read model query to the application read model query.
    /// </summary>
    /// <param name="query">The database read model query to project.</param>
    /// <returns>The projected read model query.</returns>
    static abstract IQueryable<TReadModel> ProjectTo(IQueryable<TDbReadModel> query);
}
