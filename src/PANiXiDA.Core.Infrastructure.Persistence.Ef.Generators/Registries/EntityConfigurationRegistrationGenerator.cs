using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Registries;

/// <summary>
/// Generates explicit entity configuration registrations for the consuming assembly.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class EntityConfigurationRegistrationGenerator : IIncrementalGenerator
{
    private const string RegistryName = "PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries.EntityConfigurationRegistry";

    private static readonly DiagnosticDescriptor InaccessibleConfiguration = new(
        "PANEFSG007", "Entity configuration registration requires accessible types",
        "Entity configuration '{0}' and its entity types must be accessible from generated code; use internal or public types instead of private, protected or file-local types",
        "EntityConfigurationRegistration", DiagnosticSeverity.Error, true);

    /// <summary>
    /// Registers discovery of concrete entity configurations in the consuming project.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var types = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (syntax, token) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, token) as INamedTypeSymbol)
            .Where(static type => type is { TypeKind: TypeKind.Class or TypeKind.Struct, IsAbstract: false })
            .Collect();

        context.RegisterSourceOutput(types.Combine(context.CompilationProvider), static (output, input) =>
            Generate(output, input.Left, input.Right));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<INamedTypeSymbol?> candidates, Compilation compilation)
    {
        var registry = compilation.GetTypeByMetadataName(RegistryName);
        var contract = compilation.GetTypeByMetadataName("Microsoft.EntityFrameworkCore.IEntityTypeConfiguration`1");
        if (registry is null || contract is null)
        {
            return;
        }

        var registrations = new StringBuilder();
        var constructors = new StringBuilder();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        var index = 0;
        foreach (var candidate in candidates.OrderBy(type => type!.ToDisplayString(), StringComparer.Ordinal))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var type = candidate!;
            if (!seen.Add(type) || IsOpenGeneric(type))
            {
                continue;
            }

            AppendConfiguration(context, compilation, type, contract, registrations, constructors, index++);
        }

        context.AddSource("EntityConfigurationRegistrations.g.cs", SourceText.From(BuildSource(registrations, constructors), Encoding.UTF8));
    }

    private static void AppendConfiguration(SourceProductionContext context, Compilation compilation, INamedTypeSymbol type,
        INamedTypeSymbol contract, StringBuilder registrations, StringBuilder constructors, int index)
    {
        var interfaces = type.AllInterfaces.Where(item => SymbolEqualityComparer.Default.Equals(item.OriginalDefinition, contract))
            .OrderBy(item => item.ToDisplayString(), StringComparer.Ordinal).ToArray();
        var constructor = type.InstanceConstructors.FirstOrDefault(item => item.Parameters.Length == 0
            && (type.TypeKind != TypeKind.Struct || !item.IsImplicitlyDeclared));
        if (interfaces.Length == 0 || constructor is null)
        {
            return;
        }

        if (!IsAccessible(type, compilation) || interfaces.Any(item => !IsAccessible(item.TypeArguments[0], compilation)))
        {
            context.ReportDiagnostic(Diagnostic.Create(InaccessibleConfiguration, type.Locations[0], type.ToDisplayString()));
            return;
        }

        var typeName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var create = "new " + typeName + "()";
        if (!compilation.IsSymbolAccessibleWithin(constructor, compilation.Assembly) || HasRequiredMembers(type))
        {
            create = "CreateConfiguration" + index + "()";
            constructors.AppendLine("    [global::System.Runtime.CompilerServices.UnsafeAccessor(global::System.Runtime.CompilerServices.UnsafeAccessorKind.Constructor)]")
                .Append("    private static extern ").Append(typeName).Append(' ').Append(create).AppendLine(";");
        }

        foreach (var implemented in interfaces)
        {
            registrations.Append("            modelBuilder.ApplyConfiguration<")
                .Append(implemented.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                .Append(">(").Append(create).AppendLine(");");
        }
    }

    private static bool IsOpenGeneric(INamedTypeSymbol type)
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

    private static bool IsAccessible(ITypeSymbol type, Compilation compilation)
    {
        for (var current = type as INamedTypeSymbol; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return false;
            }
        }

        return compilation.IsSymbolAccessibleWithin(type, compilation.Assembly);
    }

    private static bool HasRequiredMembers(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers().Any(member => member is IPropertySymbol { IsRequired: true } or IFieldSymbol { IsRequired: true }))
            {
                return true;
            }
        }

        return false;
    }

    private static string BuildSource(StringBuilder registrations, StringBuilder constructors)
    {
        return $$"""
            // <auto-generated />
            #nullable enable
            #pragma warning disable CA2255

            file static class GeneratedEntityConfigurationRegistrations
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Initialize()
                {
                    global::{{RegistryName}}.RegisterAssembly(
                        typeof(GeneratedEntityConfigurationRegistrations).Assembly,
                        ConfigureEntities);
                }

                private static void ConfigureEntities(global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder)
                {
                    try
                    {
            {{registrations}}
                    }
                    catch (global::System.Exception exception)
                    {
                        throw new global::System.Reflection.TargetInvocationException(exception);
                    }
                }

            {{constructors}}
            }

            """;
    }
}
