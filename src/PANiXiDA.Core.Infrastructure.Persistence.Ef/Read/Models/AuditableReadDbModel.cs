namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

/// <summary>
/// Represents the base database read model with audit timestamps and soft-delete metadata.
/// </summary>
/// <typeparam name="TId">The read model identifier type.</typeparam>
public abstract class AuditableReadDbModel<TId> : ReadDbModel<TId>
    where TId : struct
{
    /// <summary>
    /// Gets or sets the UTC timestamp when the row was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the row was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC timestamp when the row was soft-deleted.
    /// </summary>
    public DateTime? DeletedAt { get; set; }
}
