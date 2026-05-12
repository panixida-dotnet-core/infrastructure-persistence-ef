using Humanizer;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class NamingExtensions
{
    public static string ToSchemaName(this Type contextType, params string[] suffixes)
    {
        return contextType.Name
            .TrimFirstMatchingSuffix(suffixes)
            .Underscore()
            .ToLowerInvariant();
    }

    public static string ToTableName(this Type modelType, params string[] suffixes)
    {
        return modelType.Name
            .TrimFirstMatchingSuffix(suffixes)
            .ToPluralTableName();
    }

    public static string ToPluralTableName(this string tableName)
    {
        return tableName
            .Pluralize(inputIsKnownToBeSingular: false)
            .Underscore()
            .ToLowerInvariant();
    }

    public static string TrimFirstMatchingSuffix(this string value, params string[] suffixes)
    {
        var suffix = suffixes
            .Where(item =>
            {
                return !string.IsNullOrWhiteSpace(item);
            })
            .OrderByDescending(item =>
            {
                return item.Length;
            })
            .FirstOrDefault(item =>
            {
                return value.EndsWith(item, StringComparison.Ordinal);
            });

        if (suffix is null)
        {
            return value;
        }

        return value[..^suffix.Length];
    }
}
