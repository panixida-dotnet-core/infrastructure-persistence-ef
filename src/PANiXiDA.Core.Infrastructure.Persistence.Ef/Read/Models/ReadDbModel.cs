namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

/// <summary>
/// Represents the base database read model with a strongly typed identifier.
/// </summary>
/// <typeparam name="TId">The read model identifier type.</typeparam>
public abstract class ReadDbModel<TId>
    where TId : struct
{
    /// <summary>
    /// Gets or sets the read model identifier.
    /// </summary>
    public TId Id { get; set; }
}
