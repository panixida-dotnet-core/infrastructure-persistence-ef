# Reflection inventory

Scope: production code in this repository after replacing repository DI discovery with Roslyn generation. Test fixtures and Roslyn's compile-time symbol analysis are excluded from runtime reflection. This is a source audit, not proof of Native AOT compatibility.

## Removed from repository DI registration

`RepositoryRegistrationExtensions` no longer calls `Assembly.GetTypes`, `Type.GetInterfaces`, or `GetGenericTypeDefinition` to discover repositories. `RepositoryRegistrationGenerator` identifies contracts by Roslyn symbol identity and emits explicit generic `AddScoped<TContract, TImplementation>` calls. The generator ships in the existing package under `analyzers/dotnet/cs`.

## Remaining runtime usage

| Location | Usage and purpose | Implication |
| --- | --- | --- |
| [`ModelBuilderExtensions.RegisterReadDbModels`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/Extensions/ModelBuilderExtensions.cs) and [`TypeExtensions.HasGenericBaseType`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/Extensions/TypeExtensions.cs) | `Assembly.GetTypes`, `BaseType`, `IsGenericType`, and `GetGenericTypeDefinition` discover `ReadDbModel<TId>` subclasses. `ModelBuilder.Entity(Type)` registers the discovered models. | Runtime assembly scanning remains for read models. It requires model metadata and is a separate candidate for generation. |
| [`WriteDbContext.OnModelCreating`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/DbContexts/WriteDbContext.cs) | `ApplyConfigurationsFromAssembly(typeof(TDbContext).Assembly)` asks EF Core to discover and instantiate `IEntityTypeConfiguration<T>` implementations. | Assembly scanning is delegated to EF Core. Direct generated `ApplyConfiguration(new Configuration())` calls could replace this independently. |
| [`AuditableReadDbModelExtensions`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/Extensions/AuditableReadDbModelExtensions.cs) | Traverses CLR base types to recognize `AuditableReadDbModel<TId>`, then calls `Expression.Property` with the name `DeletedAt` and builds an untyped lambda. | Runtime type/member metadata is still needed for soft-delete filters. Expressions are passed to EF; this code does not call `Compile`. |
| [`NamingExtensions`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/Extensions/NamingExtensions.cs) | Reads `Type.Name` to derive schema/table names. | Metadata access only; no member discovery or dynamic instantiation. |
| [`ServiceCollectionExtensions`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/DependencyInjection/ServiceCollectionExtensions.cs) and [`GeneratedRepositoryRegistry`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef/DependencyInjection/GeneratedRepositoryRegistry.cs) | Uses `typeof(TDbContext).Assembly` to select generated callbacks and `assembly.ManifestModule.ModuleHandle` with `RuntimeHelpers.RunModuleConstructor` to ensure the generated initializer has run. Missing-registration errors read `Assembly.FullName`. | Small runtime metadata/bootstrap bridge retained to preserve existing generic DI entry points. It does not enumerate types or inspect repository members. |
| Code emitted by [`RepositoryRegistrationGenerator`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators/DependencyInjection/RepositoryRegistrationGenerator.cs) | Uses `typeof` tokens for DI registration and `Type.FullName` in duplicate-registration errors. | Type identity and diagnostic metadata only; no repository discovery or `Activator` call. |
| Sorting code emitted by [`ConstructorProjection`](../src/PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators/Read/Sorting/ConstructorProjection.cs) | Reads `NewExpression.Constructor` and `MemberExpression.Member` from typed expression templates, compares constructors, and calls `Expression.New` with known members to make positional projections translatable. | Uses `ConstructorInfo`/`MemberInfo` already present in expressions. No runtime assembly/member scanning, `MakeGenericMethod`, or `Compile`; the existing generated code includes trimming annotations. |

## Dependencies and boundaries

Generated `AddScoped<TContract, TImplementation>` calls still use Microsoft DI's normal constructor activation when services are resolved. EF Core and Npgsql retain their own model construction, query translation, and materialization behavior. Replacing repository discovery does not replace those dependency internals.

The sorting generator's `GetMembers`, `InstanceConstructors`, and `AllInterfaces` calls inspect Roslyn symbols during compilation; they are not application runtime reflection. Ordinary EF model metadata iteration (`GetEntityTypes`, `FindProperty`, and similar calls) also should not be confused with scanning CLR assemblies.

No Native AOT publish was performed for this change. A representative consuming application must validate the EF model and query paths separately; EF's [Native AOT guidance](https://learn.microsoft.com/en-us/ef/core/performance/nativeaot-and-precompiled-queries) describes its additional requirements and experimental limitations.
