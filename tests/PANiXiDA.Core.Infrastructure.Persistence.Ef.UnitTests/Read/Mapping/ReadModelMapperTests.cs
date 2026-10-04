using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Read.Mapping;

public sealed class ReadModelMapperTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
    ];

    [Theory(DisplayName = "Mapper accepts reference and value projections implementing IReadModel")]
    [InlineData("sealed record")]
    [InlineData("readonly record struct")]
    public void Compile_WhenProjectionImplementsReadModel_Succeeds(string declaration)
    {
        var source = $"public {declaration} Projection(int Id) : IReadModel;";

        var errors = Compile(source);

        errors.Should().BeEmpty();
    }

    [Theory(DisplayName = "Mapper rejects projections that do not implement IReadModel")]
    [InlineData("sealed record", "CS0311")]
    [InlineData("readonly record struct", "CS0315")]
    public void Compile_WhenProjectionDoesNotImplementReadModel_Fails(string declaration, string diagnosticId)
    {
        var source = $"public {declaration} Projection(int Id);";

        var errors = Compile(source);

        errors.Should().ContainSingle().Which.Id.Should().Be(diagnosticId);
    }

    private static Diagnostic[] Compile(string projection)
    {
        var source = """
            using System.Linq;
            using PANiXiDA.Core.Application.Querying;
            using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Mapping;
            using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

            public sealed class DatabaseModel : ReadDbModel<int> { }
            public sealed class Mapper : IReadModelMapper<int, DatabaseModel, Projection>
            {
                public static IQueryable<Projection> ProjectTo(IQueryable<DatabaseModel> query)
                    => query.Select(model => new Projection(model.Id));
            }

            """ + projection;
        var syntax = CSharpSyntaxTree.ParseText(source, cancellationToken: TestContext.Current.CancellationToken);
        var compilation = CSharpCompilation.Create("ReadModelMapperConsumer", [syntax], References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation.GetDiagnostics(TestContext.Current.CancellationToken)
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .ToArray();
    }
}
