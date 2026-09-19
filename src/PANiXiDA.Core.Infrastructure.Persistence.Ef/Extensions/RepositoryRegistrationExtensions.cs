using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class RepositoryRegistrationExtensions
{
    public static IServiceCollection AddWriteRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        GeneratedRepositoryRegistry.GetRegistration(assembly).RegisterWriteRepositories(serviceCollection);
        return serviceCollection;
    }

    public static IServiceCollection AddReadRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(serviceCollection);
        GeneratedRepositoryRegistry.GetRegistration(assembly).RegisterReadRepositories(serviceCollection);
        return serviceCollection;
    }
}
