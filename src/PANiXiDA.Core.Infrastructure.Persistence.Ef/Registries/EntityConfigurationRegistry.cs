using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

/// <summary>
/// Connects generated entity configurations to the write DbContext.
/// This API is intended for source-generated code.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class EntityConfigurationRegistry
{
    private static readonly ConditionalWeakTable<Assembly, Action<ModelBuilder>> Registrations = [];

    /// <summary>
    /// Registers the generated entity configuration callback for an assembly.
    /// </summary>
    /// <param name="assembly">The assembly containing the entity configurations.</param>
    /// <param name="configureEntities">The callback that applies entity configurations.</param>
    /// <exception cref="ArgumentNullException">An argument is null.</exception>
    /// <exception cref="ArgumentException">The assembly already has a generated registration.</exception>
    public static void RegisterAssembly(Assembly assembly, Action<ModelBuilder> configureEntities)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        ArgumentNullException.ThrowIfNull(configureEntities);

        Registrations.Add(assembly, configureEntities);
    }

    internal static Action<ModelBuilder> GetRegistration(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);

        if (Registrations.TryGetValue(assembly, out var registration))
        {
            return registration;
        }

        throw new InvalidOperationException(
            $"Generated entity configuration registration was not found for assembly '{assembly.FullName}'. " +
            "Reference PANiXiDA.Core.Infrastructure.Persistence.Ef with its analyzers in the DbContext project and rebuild it.");
    }
}
