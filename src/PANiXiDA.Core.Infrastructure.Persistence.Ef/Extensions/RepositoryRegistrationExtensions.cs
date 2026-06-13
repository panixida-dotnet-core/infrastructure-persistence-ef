using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;

using System.Reflection;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;

internal static class RepositoryRegistrationExtensions
{
    public static IServiceCollection AddWriteRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        return AddRepositoryImplementationsFromAssembly(
            serviceCollection,
            assembly,
            IsWriteRepositoryContract);
    }

    public static IServiceCollection AddReadRepositoryImplementationsFromAssembly(
        this IServiceCollection serviceCollection,
        Assembly assembly)
    {
        return AddRepositoryImplementationsFromAssembly(
            serviceCollection,
            assembly,
            IsReadRepositoryContract);
    }

    private static IServiceCollection AddRepositoryImplementationsFromAssembly(
        IServiceCollection serviceCollection,
        Assembly assembly,
        Func<Type, bool> isRepositoryContract)
    {
        foreach (var repositoryImplementation in assembly.GetTypes())
        {
            if (!repositoryImplementation.IsClass
                || repositoryImplementation.IsAbstract
                || repositoryImplementation.IsGenericTypeDefinition)
            {
                continue;
            }

            var repositoryInterfaces = repositoryImplementation.GetInterfaces()
                .Where(isRepositoryContract)
                .ToArray();

            foreach (var repositoryInterface in repositoryInterfaces)
            {
                EnsureNotRegistered(serviceCollection, repositoryInterface, repositoryImplementation);
                serviceCollection.AddScoped(repositoryInterface, repositoryImplementation);
            }
        }

        return serviceCollection;
    }

    private static bool IsWriteRepositoryContract(Type interfaceType)
    {
        return IsSupportedContract(
            interfaceType,
            typeof(IRepository<,>));
    }

    private static bool IsReadRepositoryContract(Type interfaceType)
    {
        return IsSupportedContract(
            interfaceType,
            typeof(IReadRepository<>));
    }

    private static bool IsSupportedContract(
        Type interfaceType,
        Type repositoryDefinition)
    {
        if (!interfaceType.IsInterface || interfaceType.IsGenericType)
        {
            return false;
        }

        return interfaceType.GetInterfaces().Any(parentInterface =>
        {
            return parentInterface.IsGenericType
                && parentInterface.GetGenericTypeDefinition() == repositoryDefinition;
        });
    }

    private static void EnsureNotRegistered(
        IServiceCollection serviceCollection,
        Type repositoryInterface,
        Type repositoryImplementation)
    {
        if (serviceCollection.Any(descriptor =>
        {
            return descriptor.ServiceType == repositoryInterface;
        }))
        {
            throw new InvalidOperationException(
                $"Repository interface '{repositoryInterface.FullName}' is already registered. " +
                $"Conflicting implementation: '{repositoryImplementation.FullName}'.");
        }
    }
}
