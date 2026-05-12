namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;

/// <summary>
/// Содержит константы, используемые в EF инфраструктуре.
/// </summary>
public static class EfConstants
{
    /// <summary>
    /// Имя строки подключения к PostgreSQL в конфигурации приложения.
    /// </summary>
    public const string PostgreSqlConnectionStringName = "PostgreSqlConnectionString";

    /// <summary>
    /// Имя shadow-свойства, содержащего дату и время создания сущности.
    /// </summary>
    public const string CreatedAt = "CreatedAt";

    /// <summary>
    /// Имя shadow-свойства, содержащего дату и время последнего изменения сущности.
    /// </summary>
    public const string UpdatedAt = "UpdatedAt";

    /// <summary>
    /// Имя shadow-свойства, содержащего дату и время мягкого удаления сущности.
    /// </summary>
    public const string DeletedAt = "DeletedAt";
}
