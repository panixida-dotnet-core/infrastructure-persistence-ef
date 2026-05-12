using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

public interface IReadModelMapper<TId, TDbReadModel, TReadModel>
    where TId : struct
    where TDbReadModel : ReadDbModel<TId>
{
    static abstract IQueryable<TReadModel> ProjectTo(IQueryable<TDbReadModel> query);
}
