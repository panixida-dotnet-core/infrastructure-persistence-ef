namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

public abstract class ReadDbModel<TId>
    where TId : struct
{
    public TId Id { get; set; }
}
