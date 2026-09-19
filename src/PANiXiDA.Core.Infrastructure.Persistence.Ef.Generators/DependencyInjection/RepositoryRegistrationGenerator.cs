using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.DependencyInjection;

/// <summary>
/// Generates scoped repository registrations for the consuming assembly.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class RepositoryRegistrationGenerator : IIncrementalGenerator
{
    private const string RegistryName = "PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection.GeneratedRepositoryRegistry";

    private static readonly DiagnosticDescriptor InaccessibleRepository = new(
        "PANEFSG004", "Repository registration requires accessible types",
        "Repository type or contract '{0}' must be accessible from generated code; use an internal or public type instead of a private, protected or file-local type",
        "RepositoryRegistration", DiagnosticSeverity.Error, true);

    /// <summary>
    /// Registers discovery of concrete repository implementations in the consuming project.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (syntax, token) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, token) as INamedTypeSymbol)
            .Where(static type => type is { TypeKind: TypeKind.Class, IsAbstract: false })
            .Collect();

        context.RegisterSourceOutput(types.Combine(context.CompilationProvider), static (output, input) =>
            Generate(output, input.Left, input.Right));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> candidates, Compilation compilation)
    {
        var registry = compilation.GetTypeByMetadataName(RegistryName);
        var writeContract = compilation.GetTypeByMetadataName("PANiXiDA.Core.Domain.Abstractions.IRepository`2");
        var readContract = compilation.GetTypeByMetadataName("PANiXiDA.Core.Application.Persistence.IReadRepository`1");
        if (registry is null || writeContract is null || readContract is null)
        {
            return;
        }

        var writeRegistrations = new StringBuilder();
        var readRegistrations = new StringBuilder();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates.OrderBy(type => type!.ToDisplayString(), StringComparer.Ordinal))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var implementation = candidate!;
            if (!seen.Add(implementation) || IsGeneric(implementation))
            {
                continue;
            }

            foreach (var contract in implementation.AllInterfaces.OrderBy(type => type.ToDisplayString(), StringComparer.Ordinal))
            {
                if (IsGeneric(contract))
                {
                    continue;
                }

                var isWrite = InheritsContract(contract, writeContract);
                var isRead = InheritsContract(contract, readContract);
                if (!isWrite && !isRead)
                {
                    continue;
                }

                var inaccessibleType = IsAccessible(implementation, compilation) ? contract : implementation;
                if (!IsAccessible(inaccessibleType, compilation))
                {
                    context.ReportDiagnostic(Diagnostic.Create(InaccessibleRepository, implementation.Locations[0], inaccessibleType.ToDisplayString()));
                    break;
                }

                if (isWrite)
                {
                    AppendRegistration(writeRegistrations, contract, implementation);
                }

                if (isRead)
                {
                    AppendRegistration(readRegistrations, contract, implementation);
                }
            }
        }

        context.AddSource("RepositoryRegistrations.g.cs", SourceText.From(BuildSource(writeRegistrations, readRegistrations), Encoding.UTF8));
    }

    private static bool InheritsContract(INamedTypeSymbol type, INamedTypeSymbol contract)
    {
        return type.AllInterfaces.Any(parent => SymbolEqualityComparer.Default.Equals(parent.OriginalDefinition, contract));
    }

    private static bool IsGeneric(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.Arity > 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAccessible(INamedTypeSymbol type, Compilation compilation)
    {
        for (var current = type; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return false;
            }
        }

        return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
    }

    private static void AppendRegistration(StringBuilder source, INamedTypeSymbol contract, INamedTypeSymbol implementation)
    {
        var contractName = contract.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var implementationName = implementation.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        source.Append("        EnsureNotRegistered(services, typeof(").Append(contractName).Append("), typeof(").Append(implementationName).AppendLine("));")
            .Append("        global::Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<")
            .Append(contractName).Append(", ").Append(implementationName).AppendLine(">(services);");
    }

    private static string BuildSource(StringBuilder writeRegistrations, StringBuilder readRegistrations)
    {
        return $$"""
            // <auto-generated />
            #nullable enable
            #pragma warning disable CA2255 // The initializer connects generated registrations to the library.

            file static class GeneratedRepositoryRegistrations
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Initialize()
                {
                    global::{{RegistryName}}.RegisterAssembly(
                        typeof(GeneratedRepositoryRegistrations).Assembly,
                        RegisterWriteRepositories,
                        RegisterReadRepositories);
                }

                private static void RegisterWriteRepositories(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
            {{writeRegistrations}}
                }

                private static void RegisterReadRepositories(global::Microsoft.Extensions.DependencyInjection.IServiceCollection services)
                {
            {{readRegistrations}}
                }

                private static void EnsureNotRegistered(
                    global::Microsoft.Extensions.DependencyInjection.IServiceCollection services,
                    global::System.Type contract,
                    global::System.Type implementation)
                {
                    foreach (var descriptor in services)
                    {
                        if (descriptor.ServiceType == contract)
                        {
                            throw new global::System.InvalidOperationException(
                                $"Repository interface '{contract.FullName}' is already registered. " +
                                $"Conflicting implementation: '{implementation.FullName}'.");
                        }
                    }
                }
            }

            """;
    }
}
