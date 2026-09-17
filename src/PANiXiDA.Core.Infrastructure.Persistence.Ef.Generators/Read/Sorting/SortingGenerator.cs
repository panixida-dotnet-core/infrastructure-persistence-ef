using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Read.Sorting;

/// <summary>
/// Generates typed sorting selectors for the projected models of read model sorting classes.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SortingGenerator : IIncrementalGenerator
{
    private static readonly DiagnosticDescriptor PartialSorting = new(
        "PANEFSG001", "Sorting requires a partial sorting class",
        "Sorting type '{0}' and its containing types must be partial to generate sorting",
        "Sorting", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor ConcreteModel = new(
        "PANEFSG002", "Sorting requires a concrete projection",
        "Sorting type '{0}' must implement exactly one IReadModelSorting contract with a concrete projected model",
        "Sorting", DiagnosticSeverity.Error, true);

    private static readonly DiagnosticDescriptor FieldCollision = new(
        "PANEFSG003", "Sorting field names are ambiguous",
        "Projected model '{0}' contains sorting paths that differ only by case: '{1}'",
        "Sorting", DiagnosticSeverity.Error, true);

    /// <summary>
    /// Registers generation for read model sorting classes declared in the consuming project.
    /// </summary>
    /// <param name="context">The generator initialization context.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var contract = context.CompilationProvider.Select(static (compilation, _) =>
            compilation.GetTypeByMetadataName("PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting.IReadModelSorting`1"));
        var sortingTypes = context.SyntaxProvider.CreateSyntaxProvider(
                static (node, _) => node is TypeDeclarationSyntax { BaseList: not null },
                static (syntax, token) => syntax.SemanticModel.GetDeclaredSymbol(syntax.Node, token) as INamedTypeSymbol)
            .Combine(contract)
            .Where(static pair => pair.Left is { IsAbstract: false }
                && pair.Right is not null && pair.Left.AllInterfaces.Any(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, pair.Right)))
            .Collect();

        context.RegisterSourceOutput(sortingTypes, static (output, candidates) => Generate(output, candidates));
    }

    private static void Generate(SourceProductionContext context, ImmutableArray<(INamedTypeSymbol? Left, INamedTypeSymbol? Right)> candidates)
    {
        var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        foreach (var (candidate, contract) in candidates)
        {
            var sortingType = candidate!;
            if (!seen.Add(sortingType))
            {
                continue;
            }

            var contracts = sortingType.AllInterfaces.Where(type => SymbolEqualityComparer.Default.Equals(type.OriginalDefinition, contract)).ToArray();
            if (contracts.Length == 1 && sortingType.FindImplementationForInterfaceMember(contracts[0].GetMembers("ApplySorting").Single()) is not null)
            {
                continue;
            }

            if (contracts.Length != 1 || contracts[0].TypeArguments[0] is not INamedTypeSymbol model || ContainsTypeParameter(model))
            {
                context.ReportDiagnostic(Diagnostic.Create(ConcreteModel, sortingType.Locations[0], sortingType.ToDisplayString()));
                continue;
            }

            var containers = ContainingTypes(sortingType).Reverse().ToArray();
            if (containers.Any(type => type.DeclaringSyntaxReferences.Any(reference =>
                reference.GetSyntax(context.CancellationToken) is not TypeDeclarationSyntax declaration
                || !declaration.Modifiers.Any(SyntaxKind.PartialKeyword) || declaration.Modifiers.Any(SyntaxKind.FileKeyword))))
            {
                context.ReportDiagnostic(Diagnostic.Create(PartialSorting, sortingType.Locations[0], sortingType.ToDisplayString()));
                continue;
            }

            var paths = new List<(string Path, string Selector)>();
            var models = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            List<string> guards = model.IsReferenceType && model.NullableAnnotation == NullableAnnotation.Annotated ? ["item == null"] : [];
            var projectionModel = model;
            var access = "item";
            if (model.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
            {
                projectionModel = (INamedTypeSymbol)model.TypeArguments[0];
                access = "item.Value";
                guards.Add("!item.HasValue");
            }

            CollectPaths(projectionModel, "", access, guards, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), (models, paths), context.CancellationToken);
            var collision = paths.GroupBy(path => path.Path, StringComparer.OrdinalIgnoreCase).FirstOrDefault(group => group.Count() > 1);
            if (collision is not null)
            {
                context.ReportDiagnostic(Diagnostic.Create(FieldCollision, sortingType.Locations[0], model.ToDisplayString(), string.Join(", ", collision.Select(path => path.Path))));
                continue;
            }

            var source = BuildSource(sortingType, model, containers, paths, ConstructorProjection.Build(models));
            var hintName = (sortingType.ContainingNamespace.IsGlobalNamespace ? "" : sortingType.ContainingNamespace.ToDisplayString() + ".")
                + string.Join("_", containers.Select(type => type.MetadataName)) + ".Sorting.g.cs";
            context.AddSource(hintName, SourceText.From(source, Encoding.UTF8));
        }
    }

    private static string BuildSource(INamedTypeSymbol sortingType, INamedTypeSymbol model, INamedTypeSymbol[] containers,
        List<(string Path, string Selector)> paths, string projectionRewriter)
    {
        var modelName = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier));
        var queryType = $"global::System.Linq.IQueryable<{modelName}>";
        var orderedType = $"global::System.Linq.IOrderedQueryable<{modelName}>";
        const string parametersType = "global::PANiXiDA.Core.Application.Querying.Sorting.SortingParameters";
        const string directionType = "global::PANiXiDA.Core.Application.Querying.Sorting.SortDirection";
        var sortingTypeName = sortingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var source = new StringBuilder("#nullable enable\n");
        if (!sortingType.ContainingNamespace.IsGlobalNamespace)
        {
            source.Append("namespace ").Append(sortingType.ContainingNamespace.ToDisplayString()).AppendLine(";");
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
            .AppendLine("/// <returns>The query ordered by client criteria and remaining defaults, or unchanged when both are empty.</returns>")
            .Append("public static ").Append(queryType).Append(" ApplySorting(").Append(queryType).Append(" query, ").Append(parametersType).AppendLine(" sortingParameters)")
            .AppendLine("{")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(query);")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(sortingParameters);")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(sortingParameters.Fields);")
            .Append("var defaultSorting = GetDefaultSorting<").Append(sortingTypeName).AppendLine(">();")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(defaultSorting);")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(defaultSorting.Fields);")
            .AppendLine("foreach (var field in global::System.Linq.Enumerable.Concat(sortingParameters.Fields, defaultSorting.Fields))")
            .AppendLine("{")
            .AppendLine("global::System.ArgumentNullException.ThrowIfNull(field);")
            .AppendLine("}")
            .AppendLine("var effectiveSorting = sortingParameters.WithDefault(defaultSorting);");

        if (projectionRewriter.Length > 0)
        {
            source.AppendLine("if (effectiveSorting.Fields.Length > 0)")
                .AppendLine("{")
                .AppendLine("var expression = new SortingProjectionRewriter().Visit(query.Expression);")
                .AppendLine("if (!global::System.Object.ReferenceEquals(expression, query.Expression))")
                .AppendLine("{")
                .Append("query = query.Provider.CreateQuery<").Append(modelName).AppendLine(">(expression);")
                .AppendLine("}\n}");
        }

        source.Append(orderedType).AppendLine("? ordered = null;")
            .AppendLine("foreach (var field in effectiveSorting.Fields)")
            .AppendLine("{")
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
            .AppendLine("return ordered ?? query;")
            .Append("static ").Append(parametersType).AppendLine(" GetDefaultSorting<TSorting>()")
            .Append("where TSorting : global::PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting.IReadModelSorting<").Append(modelName).AppendLine(">")
            .AppendLine("{")
            .AppendLine("return TSorting.DefaultSorting;")
            .AppendLine("}");

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
        source.Append(projectionRewriter);
        foreach (var _ in containers)
        {
            source.AppendLine("}");
        }

        return source.ToString();
    }

    private static void CollectPaths(INamedTypeSymbol model, string prefix, string access, List<string> guards,
        HashSet<INamedTypeSymbol> ancestors, (HashSet<INamedTypeSymbol> Models, List<(string Path, string Selector)> Paths) projection, CancellationToken cancellationToken)
    {
        var canDescend = ancestors.Add(model.OriginalDefinition);

        projection.Models.Add(model);
        foreach (var property in Properties(model))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (property.IsStatic || property.IsIndexer || property.GetMethod?.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            CollectPropertyPath(property, prefix, access, guards, (ancestors, canDescend), projection, cancellationToken);
        }

        if (canDescend)
        {
            ancestors.Remove(model.OriginalDefinition);
        }
    }

    private static void CollectPropertyPath(IPropertySymbol property, string prefix, string access, List<string> guards,
        (HashSet<INamedTypeSymbol> Ancestors, bool CanDescend) traversal,
        (HashSet<INamedTypeSymbol> Models, List<(string Path, string Selector)> Paths) projection, CancellationToken cancellationToken)
    {
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
            projection.Paths.Add((path, selector));
            return;
        }

        if (!traversal.CanDescend || type is not INamedTypeSymbol nested)
        {
            return;
        }

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

            CollectPaths(nested, path + ".", propertyAccess + (nullable ? ".Value" : ""), nestedGuards, traversal.Ancestors, projection, cancellationToken);
        }
    }

    internal static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol model)
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

    internal static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol model)
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
