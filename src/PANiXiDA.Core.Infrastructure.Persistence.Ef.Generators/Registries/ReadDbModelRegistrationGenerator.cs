using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Registries;

/// <summary>
/// Generates read model mappings and soft-delete filters for the consuming assembly.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ReadDbModelRegistrationGenerator : IIncrementalGenerator
{
    private const string RegistryName = "PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries.ReadDbModelRegistry";
    private const string ModelSuffix = "ReadDbModel";

    private static readonly DiagnosticDescriptor InaccessibleModel = new(
        "PANEFSG005", "Read model registration requires accessible types",
        "Read model '{0}' must be accessible from generated code; use an internal or public type instead of a private, protected or file-local type",
        "ReadDbModelRegistration", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor OpenGenericModel = new(
        "PANEFSG006", "Read model registration requires closed types",
        "Read model '{0}' cannot be registered as an open generic type; make the generic base abstract and declare a concrete non-generic model",
        "ReadDbModelRegistration", DiagnosticSeverity.Error, true);

    /// <summary>
    /// Registers discovery of concrete read models in the consuming project.
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
        var readModel = compilation.GetTypeByMetadataName("PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models.ReadDbModel`1");
        var auditableModel = compilation.GetTypeByMetadataName("PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models.AuditableReadDbModel`1");
        if (registry is null || readModel is null || auditableModel is null)
        {
            return;
        }

        var registrations = new StringBuilder();
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in candidates.OrderBy(type => type!.ToDisplayString(), StringComparer.Ordinal))
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var model = candidate!;
            if (!seen.Add(model) || !InheritsModel(model, readModel))
            {
                continue;
            }

            var diagnostic = GetUnsupportedModelDiagnostic(model, compilation);
            if (diagnostic is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(diagnostic, model.Locations[0], model.ToDisplayString()));
                continue;
            }

            AppendRegistration(registrations, model, InheritsModel(model, auditableModel));
        }

        context.AddSource("ReadDbModelRegistrations.g.cs", SourceText.From(BuildSource(registrations), Encoding.UTF8));
    }

    private static bool InheritsModel(INamedTypeSymbol type, INamedTypeSymbol model)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, model))
            {
                return true;
            }
        }

        return false;
    }

    private static DiagnosticDescriptor? GetUnsupportedModelDiagnostic(INamedTypeSymbol model, Compilation compilation)
    {
        for (var current = model; current is not null; current = current.ContainingType)
        {
            if (current.IsFileLocal)
            {
                return InaccessibleModel;
            }

            if (current.Arity > 0)
            {
                return OpenGenericModel;
            }
        }

        return compilation.IsSymbolAccessibleWithin(model, compilation.Assembly) ? null : InaccessibleModel;
    }

    private static void AppendRegistration(StringBuilder source, INamedTypeSymbol model, bool isAuditable)
    {
        var modelName = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var tableName = model.Name.EndsWith(ModelSuffix, StringComparison.Ordinal)
            ? model.Name.Substring(0, model.Name.Length - ModelSuffix.Length)
            : model.Name;

        source.AppendLine("        {")
            .Append("            var entity = modelBuilder.Entity<").Append(modelName).AppendLine(">();")
            .Append("            var tableName = ").Append(SymbolDisplay.FormatLiteral(tableName, quote: true))
            .AppendLine(".Pluralize(inputIsKnownToBeSingular: false).Underscore().ToLowerInvariant();")
            .AppendLine("            if (excludeFromMigrations)")
            .AppendLine("            {")
            .AppendLine("                entity.ToTable(tableName, schemaName, table => table.ExcludeFromMigrations());")
            .AppendLine("            }")
            .AppendLine("            else")
            .AppendLine("            {")
            .AppendLine("                entity.ToTable(tableName, schemaName);")
            .AppendLine("            }");

        if (isAuditable)
        {
            source.AppendLine("            entity.HasQueryFilter(item => item.DeletedAt == null);");
        }

        source.AppendLine("        }");
    }

    private static string BuildSource(StringBuilder registrations)
    {
        return $$"""
            // <auto-generated />
            #nullable enable
            #pragma warning disable CA2255

            using global::Humanizer;
            using global::Microsoft.EntityFrameworkCore;

            file static class GeneratedReadDbModelRegistrations
            {
                [global::System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Initialize()
                {
                    global::{{RegistryName}}.RegisterAssembly(
                        typeof(GeneratedReadDbModelRegistrations).Assembly,
                        RegisterReadDbModels);
                }

                private static void RegisterReadDbModels(
                    global::Microsoft.EntityFrameworkCore.ModelBuilder modelBuilder,
                    string? schemaName,
                    bool excludeFromMigrations)
                {
            {{registrations}}
                }
            }

            """;
    }
}
