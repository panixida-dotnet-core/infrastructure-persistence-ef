namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

/// <summary>
/// Defines constants used by the Entity Framework Core persistence infrastructure.
/// </summary>
public static class EfConstants
{
    /// <summary>
    /// The application configuration connection string name used for PostgreSQL.
    /// </summary>
    public const string PostgreSqlConnectionStringName = "PostgreSqlConnectionString";

    /// <summary>
    /// The shadow property name that stores the entity creation timestamp.
    /// </summary>
    public const string CreatedAt = "CreatedAt";

    /// <summary>
    /// The shadow property name that stores the latest entity update timestamp.
    /// </summary>
    public const string UpdatedAt = "UpdatedAt";

    /// <summary>
    /// The shadow property name that stores the soft-delete timestamp.
    /// </summary>
    public const string DeletedAt = "DeletedAt";
}
