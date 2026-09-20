using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class RepositoryRegistrationExtensions
{
    public static IServiceCollection AddWriteRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        RepositoryRegistry.GetRegistration(assembly).RegisterWriteRepositories(serviceCollection);
        return serviceCollection;
    }

    public static IServiceCollection AddReadRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        RepositoryRegistry.GetRegistration(assembly).RegisterReadRepositories(serviceCollection);
        return serviceCollection;
    }
}
