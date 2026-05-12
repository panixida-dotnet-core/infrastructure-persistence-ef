namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class TypeExtensions
{
    public static bool HasGenericBaseType(this Type type, Type genericBaseTypeDefinition)
    {
        var baseType = type.BaseType;

        while (baseType is not null)
        {
            if (baseType.IsGenericType
                && baseType.GetGenericTypeDefinition() == genericBaseTypeDefinition)
            {
                return true;
            }

            baseType = baseType.BaseType;
        }

        return false;
    }
}
