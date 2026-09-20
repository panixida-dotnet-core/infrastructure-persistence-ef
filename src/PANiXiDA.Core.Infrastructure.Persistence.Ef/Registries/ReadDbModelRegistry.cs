using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

/// <summary>
/// Connects generated read model registrations to the read DbContext.
/// This API is intended for source-generated code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class ReadDbModelRegistry
{
    private static readonly ConditionalWeakTable<Assembly, Action<ModelBuilder, string?, bool>> Registrations = [];

    /// <summary>
    /// Registers the generated read model callback for an assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the read models.</param>
    /// <param name="registerReadDbModels">The callback that configures read models, their tables and query filters.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The assembly already has a generated registration.</exception>
    public static void RegisterAssembly(Assembly assembly, Action<ModelBuilder, string?, bool> registerReadDbModels)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(registerReadDbModels);

        Registrations.Add(assembly, registerReadDbModels);
    }

    internal static Action<ModelBuilder, string?, bool> GetRegistration(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        if (Registrations.TryGetValue(assembly, out var registration))
        {
            return registration;
        }

        throw new InvalidOperationException(
            $"Generated read model registration was not found for assembly '{assembly.FullName}'. " +
            "Reference PANiXiDA.Core.Infrastructure.Persistence.Ef with its analyzers in the DbContext project and rebuild it.");
    }
}
