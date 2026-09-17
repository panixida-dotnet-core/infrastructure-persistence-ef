using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Read.Sorting;

internal static class ConstructorProjection
{
    private const string Expressions = "global::System.Linq.Expressions.";

    internal static string Build(IEnumerable<INamedTypeSymbol> models)
    {
        var templates = new StringBuilder();
        var mappings = new StringBuilder();
        var index = 0;
        foreach (var model in models.Where(model => !model.IsAbstract
                         && (model.IsRecord || model.TypeKind == TypeKind.Struct && model.DeclaringSyntaxReferences.Length == 0))
                     .OrderBy(model => model.ToDisplayString(), StringComparer.Ordinal))
        {
            var properties = SortingGenerator.Properties(model).ToArray();
            foreach (var constructor in model.InstanceConstructors.Where(IsPositionalConstructor))
            {
                var members = constructor.Parameters.Select(parameter => properties.FirstOrDefault(property => MatchesParameter(property, parameter))).ToArray();
                if (members.Any(member => member is null))
                {
                    continue;
                }

                AppendTemplate(templates, model, constructor, members, properties, index);
                mappings.Append("if (visited.Constructor == Projection").Append(index).AppendLine(".Constructor)")
                    .AppendLine("{")
                    .Append("return Projection").Append(index).AppendLine(".Update(visited.Arguments);")
                    .AppendLine("}");
                index++;
            }
        }

        if (index == 0)
        {
            return "";
        }

        return $$"""
            private sealed class SortingProjectionRewriter : {{Expressions}}ExpressionVisitor
            {
            {{templates}}
            protected override {{Expressions}}Expression VisitNew({{Expressions}}NewExpression node)
            {
                var visited = ({{Expressions}}NewExpression)base.VisitNew(node);
                if (visited.Members is not null)
                {
                    return visited;
                }
                {{mappings}}
                return visited;
            }
            }

            """;
    }

    private static bool IsPositionalConstructor(IMethodSymbol constructor)
    {
        return constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length > 0
            && constructor.Parameters.All(parameter => parameter.RefKind == RefKind.None)
            && (constructor.DeclaringSyntaxReferences.Length == 0
                || constructor.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is RecordDeclarationSyntax { ParameterList: not null }));
    }

    private static bool MatchesParameter(IPropertySymbol property, IParameterSymbol parameter)
    {
        return property.Name == parameter.Name
            && (parameter.DeclaringSyntaxReferences.Length == 0
                ? !property.IsStatic && property.GetMethod?.DeclaredAccessibility == Accessibility.Public
                    && SymbolEqualityComparer.Default.Equals(property.Type, parameter.Type)
                : SymbolEqualityComparer.Default.Equals(property.ContainingType, parameter.ContainingSymbol.ContainingType)
                    && property.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax() is ParameterSyntax));
    }

    private static void AppendTemplate(StringBuilder source, INamedTypeSymbol model, IMethodSymbol constructor,
        IPropertySymbol?[] members, IPropertySymbol[] properties, int index)
    {
        var modelName = model.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        source.Append("private static readonly ").Append(Expressions).Append("NewExpression Projection").Append(index)
            .Append(" = CreateProjection").Append(index).AppendLine("();")
            .Append("[global::System.Diagnostics.CodeAnalysis.DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties, typeof(")
            .Append(modelName).AppendLine("))]")
            .AppendLine("[global::System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(\"Trimming\", \"IL2026\", Justification = \"Typed expressions preserve the constructor; DynamicDependency preserves all mapped property accessors.\")]")
            .Append("private static ").Append(Expressions).Append("NewExpression CreateProjection").Append(index).AppendLine("()")
            .AppendLine("{")
            .Append(Expressions).Append("Expression<global::System.Func<").Append(modelName).Append(">> template = () => new ").Append(modelName).Append('(')
            .Append(string.Join(", ", constructor.Parameters.Select(parameter => "default(" + parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")!")))
            .Append(')');
        var initializedMembers = properties.Where(property => property.IsRequired).Select(property => property.Name)
            .Concat(SortingGenerator.BaseTypes(model).SelectMany(type => type.GetMembers()).OfType<IFieldSymbol>()
                .Where(field => field.IsRequired).Select(field => field.Name))
            .Distinct(StringComparer.Ordinal).ToArray();
        if (initializedMembers.Length > 0)
        {
            source.Append(" { ").Append(string.Join(", ", initializedMembers.Select(name => "@" + name + " = default!"))).Append(" }");
        }

        source.AppendLine(";")
            .Append("var constructor = template.Body is ").Append(Expressions).Append("MemberInitExpression initializer ? initializer.NewExpression : (")
            .Append(Expressions).AppendLine("NewExpression)template.Body;")
            .Append("return ").Append(Expressions).AppendLine("Expression.New(constructor.Constructor!, constructor.Arguments, [");
        foreach (var member in members)
        {
            source.Append("((").Append(Expressions).Append("MemberExpression)((").Append(Expressions)
                .Append("Expression<global::System.Func<").Append(modelName).Append(", ")
                .Append(member!.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
                    SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier)))
                .Append(">>)(item => item.@").Append(member.Name).AppendLine(")).Body).Member,");
        }

        source.AppendLine("]);\n}");
    }
}
