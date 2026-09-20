using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Registrations;

/// <summary>
/// Connects generated assembly registrations to the persistence registration extensions.
/// This API is intended for source-generated code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class RepositoryRegistry
{
    private static readonly ConditionalWeakTable<Assembly, Registration> Registrations = new();

    /// <summary>
    /// Registers the generated repository callbacks for an assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the repository implementations.</param>
    /// <param name="registerWriteRepositories">The callback that registers write repositories.</param>
    /// <param name="registerReadRepositories">The callback that registers read repositories.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The assembly already has a generated registration.</exception>
    public static void RegisterAssembly(
        Assembly assembly,
        Action<IServiceCollection> registerWriteRepositories,
        Action<IServiceCollection> registerReadRepositories)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(registerWriteRepositories);
        ArgumentNullException.ThrowIfNull(registerReadRepositories);

        Registrations.Add(assembly, new Registration(registerWriteRepositories, registerReadRepositories));
    }

    internal static Registration GetRegistration(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        if (Registrations.TryGetValue(assembly, out var registration))
        {
            return registration;
        }

        throw new InvalidOperationException(
            $"Generated repository registration was not found for assembly '{assembly.FullName}'. " +
            "Reference PANiXiDA.Core.Infrastructure.Persistence.Ef with its analyzers in the DbContext project and rebuild it.");
    }

    internal sealed record Registration(
        Action<IServiceCollection> RegisterWriteRepositories,
        Action<IServiceCollection> RegisterReadRepositories);
}
