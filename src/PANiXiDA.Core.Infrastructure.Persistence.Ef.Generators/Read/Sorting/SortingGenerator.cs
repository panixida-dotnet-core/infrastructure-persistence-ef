using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Read.Sorting;

/// <summary>
/// Generates typed sorting selectors for the projected models of read model mappers.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SortingGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor PartialMapper = new(
        "PANEFSG001", "Sorting requires a partial mapper",
        "Mapper '{0}' and its containing types must be partial to generate sorting",
        "Sorting", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor ConcreteModel = new(
        "PANEFSG002", "Sorting requires a concrete projection",
        "Mapper '{0}' must implement exactly one IReadModelMapper contract with a concrete projected model",
        "Sorting", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor FieldCollision = new(
        "PANEFSG003", "Sorting field names are ambiguous",
        "Projected model '{0}' contains sorting paths that differ only by case: '{1}'",
        "Sorting", DiagnosticSeverity.Error, true);

    /// <summary>
    /// Registers generation for read model mappers declared in the consuming project.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contract = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.IReadModelMapper`3"));
        var mappers = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (syntax, token) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, token) as INamedTypeSymbol)
            .Combine(contract)
            .Where(static pair => pair.Left is { IsAbstract: false, TypeKind: TypeKind.Class or TypeKind.Struct }
                && pair.Right is not null && pair.Left.AllInterfaces.Any(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, pair.Right)))
            .Collect();

        context.RegisterSourceOutput(mappers, static (output, candidates) => Generate(output, candidates));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<(INamedTypeSymbol? Left, INamedTypeSymbol? Right)> candidates)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var (candidate, contract) in candidates)
        {
            var mapper = candidate!;
            if (!seen.Add(mapper))
            {
                continue;
            }

            var contracts = mapper.AllInterfaces.Where(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, contract)).ToArray();
            if (contracts.Length == 1 && mapper.FindImplementationForInterfaceMember(contracts[0].GetMembers("ApplySorting").Single()) is not null)
            {
                continue;
            }

            if (contracts.Length != 1 || contracts[0].TypeArguments[2] is not INamedTypeSymbol model || ContainsTypeParameter(model))
            {
                context.ReportDiagnostic(Diagnostic.Create(ConcreteModel, mapper.Locations[0], mapper.ToDisplayString()));
                continue;
            }

            var containers = ContainingTypes(mapper).Reverse().ToArray();
            if (containers.Any(type => type.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(context.CancellationToken) is not TypeDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || declaration.Modifiers.Any(SyntaxKind.FileKeyword))))
            {
                context.ReportDiagnostic(Diagnostic.Create(PartialMapper, mapper.Locations[0], mapper.ToDisplayString()));
                continue;
            }

            var paths = new List<(string Path, string Selector)>();
            CollectPaths(model, "", "item", [], new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), paths, context.CancellationToken);
            var collision = paths.GroupBy(path => path.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (collision is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(FieldCollision, mapper.Locations[0], model.ToDisplayString(), string.Join(", ", collision.Select(path => path.Path))));
                continue;
            }

            var source = BuildSource(mapper, model, containers, paths);
            var hintName = (mapper.ContainingNamespace.IsGlobalNamespace ? "" : mapper.ContainingNamespace.ToDisplayString() + ".")
                + string.Join("_", containers.Select(type => type.MetadataName)) + ".Sorting.g.cs";
            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string BuildSource(INamedTypeSymbol mapper, INamedTypeSymbol model, INamedTypeSymbol[] containers,
        List<(string Path, string Selector)> paths)
    {
        var modelName = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var queryType = $"global::System.Linq.IQueryable<{modelName}>";
        var orderedType = $"global::System.Linq.IOrderedQueryable<{modelName}>";
        const string sortingType = "global::PANiXiDA.Core.Application.Querying.Sorting.SortingParameters";
        const string directionType = "global::PANiXiDA.Core.Application.Querying.Sorting.SortDirection";
        var source = new StringBuilder("#nullable enable\n");
        if (!mapper.ContainingNamespace.IsGlobalNamespace)
        {
            source.Append("namespace ").Append(mapper.ContainingNamespace.ToDisplayString()).AppendLine(";");
        }

        foreach (var type in containers)
        {
            source.Append("partial ").Append(type.IsRecord ? "record " : "")
                .Append(type.TypeKind == TypeKind.Struct ? "struct " : "class ").Append('@').Append(type.Name);
            if (type.TypeParameters.Length > 0)
            {
                source.Append('<').Append(string.Join(", ", type.TypeParameters.Select(parameter => "@" + parameter.Name))).Append('>');
            }

            source.AppendLine("\n{");
        }

        source.AppendLine("/// <summary>Applies sorting by the projected model's scalar property paths without runtime member discovery.</summary>")
            .AppendLine("/// <param name=\"query\">The projected query to sort.</param>")
            .AppendLine("/// <param name=\"sortingParameters\">Validated sorting criteria in their order of precedence.</param>")
            .AppendLine("/// <returns>The sorted query, or the original query when sorting is empty.</returns>")
            .Append("public static ").Append(queryType).Append(" ApplySorting(").Append(queryType).Append(" query, ").Append(sortingType).AppendLine(" sortingParameters)")
            .AppendLine("{")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(query);")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(sortingParameters);")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(sortingParameters.Fields);")
            .Append(orderedType).AppendLine("? ordered = null;")
            .AppendLine("foreach (var field in sortingParameters.Fields)")
            .AppendLine("{")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(field);")
            .Append("if (field.Order is not (").Append(directionType).Append(".Asc or ").Append(directionType).AppendLine(".Desc))")
            .AppendLine("{")
            .AppendLine("throw new global::System.ArgumentOutOfRangeException(nameof(sortingParameters), field.Order, \"Unsupported sorting direction.\");")
            .AppendLine("}")
            .AppendLine("ordered = field.Field switch")
            .AppendLine("{");

        foreach (var path in paths.OrderBy(path => path.Path, StringComparer.Ordinal))
        {
            source.Append("var name when global::System.String.Equals(name, ").Append(SymbolDisplay.FormatLiteral(path.Path, true))
                .Append(", global::System.StringComparison.OrdinalIgnoreCase) => Order(query, ordered, item => ")
                .Append(path.Selector).AppendLine(", field.Order),");
        }

        source.AppendLine("_ => throw new global::System.ArgumentException($\"Sorting field '{field.Field}' is not supported.\", nameof(sortingParameters))")
            .AppendLine("};\n}")
            .AppendLine("return ordered ?? query;");

        if (paths.Count > 0)
        {
            source.Append("static ").Append(orderedType).Append(" Order<TKey>(").Append(queryType).Append(" source, ")
                .Append(orderedType).Append("? previous, global::System.Linq.Expressions.Expression<global::System.Func<")
                .Append(modelName).Append(", TKey>> selector, ").Append(directionType).AppendLine(" direction)")
                .AppendLine("{")
                .AppendLine("if (previous is null)")
                .AppendLine("{")
                .Append("return direction == ").Append(directionType).AppendLine(".Desc")
                .AppendLine("? global::System.Linq.Queryable.OrderByDescending(source, selector)")
                .AppendLine(": global::System.Linq.Queryable.OrderBy(source, selector);")
                .AppendLine("}")
                .Append("return direction == ").Append(directionType).AppendLine(".Desc")
                .AppendLine("? global::System.Linq.Queryable.ThenByDescending(previous, selector)")
                .AppendLine(": global::System.Linq.Queryable.ThenBy(previous, selector);")
                .AppendLine("}");
        }

        source.AppendLine("}");
        foreach (var _ in containers)
        {
            source.AppendLine("}");
        }

        return source.ToString();
    }

    private static void CollectPaths(INamedTypeSymbol model, string prefix, string access, List<string> guards,
        HashSet<INamedTypeSymbol> ancestors, List<(string Path, string Selector)> paths, CancellationToken cancellationToken)
    {
        if (!ancestors.Add(model.OriginalDefinition))
        {
            return;
        }

        foreach (var property in Properties(model))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.IsStatic || property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            var type = property.Type;
            var nullable = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T };
            if (nullable)
            {
                type = ((INamedTypeSymbol)type).TypeArguments[0];
            }

            var path = prefix + property.Name;
            var propertyAccess = access + ".@" + property.Name;
            if (IsScalar(type))
            {
                var resultType = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                var selector = guards.Count == 0 ? propertyAccess
                    : string.Join(" || ", guards) + " ? default(" + resultType + "?) : " + propertyAccess;
                paths.Add((path, selector));
            }
            else if (type is INamedTypeSymbol nested)
            {
                var ns = nested.ContainingNamespace.ToDisplayString();
                if (nested.SpecialType != SpecialType.System_Collections_IEnumerable
                    && !nested.AllInterfaces.Any(contract => contract.SpecialType == SpecialType.System_Collections_IEnumerable)
                    && ns != "System" && !ns.StartsWith("System.", StringComparison.Ordinal))
                {
                    var nestedGuards = new List<string>(guards);
                    if (type.IsReferenceType || nullable)
                    {
                        nestedGuards.Add(propertyAccess + " == null");
                    }

                    CollectPaths(nested, path + ".", propertyAccess + (nullable ? ".Value" : ""), nestedGuards, ancestors, paths, cancellationToken);
                }
            }
        }

        ancestors.Remove(model.OriginalDefinition);
    }

    private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol model)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var types = model.TypeKind == TypeKind.Interface ? new[] { model }.Concat(model.AllInterfaces) : BaseTypes(model);
        foreach (var type in types)
        {
            foreach (var member in type.GetMembers())
            {
                if (seen.Add(member.Name) && member is IPropertySymbol property)
                {
                    yield return property;
                }
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol model)
    {
        for (var type = model; type is not null; type = type.BaseType)
        {
            yield return type;
        }
    }

    private static IEnumerable<INamedTypeSymbol> ContainingTypes(INamedTypeSymbol model)
    {
        for (var type = model; type is not null; type = type.ContainingType)
        {
            yield return type;
        }
    }

    private static bool ContainsTypeParameter(ITypeSymbol type)
    {
        return type switch
        {
            INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter)
                || named.ContainingType is not null && ContainsTypeParameter(named.ContainingType),
            IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
            _ => true
        };
    }

    private static bool IsScalar(ITypeSymbol type)
    {
        return type.TypeKind == TypeKind.Enum
            || type.SpecialType is >= SpecialType.System_Boolean and <= SpecialType.System_Double
                or SpecialType.System_String or SpecialType.System_DateTime
            || type.ToDisplayString() is "System.Guid" or "System.DateTimeOffset" or "System.DateOnly" or "System.TimeOnly" or "System.TimeSpan";
    }
}
