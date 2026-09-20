using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registrations;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class RepositoryRegistrationExtensions
{
    public static IServiceCollection AddWriteRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        RepositoryRegistr.GetRegistration(assembly).RegisterWriteRepositories(serviceCollection);
        return serviceCollection;
    }

    public static IServiceCollection AddReadRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        RepositoryRegistr.GetRegistration(assembly).RegisterReadRepositories(serviceCollection);
        return serviceCollection;
    }
}
