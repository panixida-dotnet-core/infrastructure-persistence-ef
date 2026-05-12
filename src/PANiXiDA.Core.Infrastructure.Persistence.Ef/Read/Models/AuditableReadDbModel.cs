namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

public abstract class AuditableReadDbModel<TId> : ReadDbModel<TId>
    where TId : struct
{
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? DeletedAt { get; set; }
}
