using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Read.Sorting;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Generators.Read.Sorting;

public sealed class SortingGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
    ];

    [Fact(DisplayName = "Sorting generator emits typed nullable nested selectors and ignores unsupported members")]
    public void Generate_EmitsTypedPropertyPaths()
    {
        var source = """
            public class BaseModel { public int Inherited { get; init; } }
            public class Model : BaseModel
            {
                public string Name { get; init; } = "";
                public Department? Department { get; init; }
                public Detail? Detail { get; init; }
                public Department Other { get; init; } = new();
                public List<string> Collection { get; init; } = [];
                public string[] Array { get; init; } = [];
                public object Object { get; init; } = new();
                public System.Type Type { get; init; } = typeof(object);
                public IntPtr Pointer { get; init; }
                public string this[int index] => "";
                public static string Static => "";
                public string WriteOnly { set { } }
                public string Private { private get; set; } = "";
            }
            public class Department
            {
                public string Name { get; init; } = "";
                public int Rank { get; init; }
                public Model? Parent { get; init; }
            }
            public record struct Detail(DateTimeOffset Date);
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            public partial class Sorting : IReadModelSorting<Model> { }
            """;

        var result = Generate(source);

        var generated = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        generated.Should().ContainAll("item.@Department.@Name", "item.@Department == null", "default(int?)", "item.@Detail.Value.@Date", "item.@Other.@Rank", "item.@Inherited");
        generated.Should().NotContain("item.@Collection").And.NotContain("item.@Array").And.NotContain("item.@Object")
            .And.NotContain("item.@Type")
            .And.NotContain("item.@Pointer")
            .And.NotContain("item.@Static").And.NotContain("item.@Private").And.NotContain("item.@WriteOnly").And.NotContain(".@Parent");
        generated.Should().NotContain("System.Reflection").And.NotContain("MakeGenericMethod").And.NotContain(".Compile(");
    }

    [Theory(DisplayName = "Sorting generator supports scalar types")]
    [InlineData("string")]
    [InlineData("bool")]
    [InlineData("int")]
    [InlineData("decimal")]
    [InlineData("double")]
    [InlineData("Guid")]
    [InlineData("DateTime")]
    [InlineData("DateTimeOffset")]
    [InlineData("DateOnly")]
    [InlineData("TimeOnly")]
    [InlineData("TimeSpan")]
    [InlineData("DayOfWeek")]
    public void Generate_SupportsScalarTypes(string type)
    {
        var result = Generate($$"""
            public record Model({{type}} Value, {{type}}? Optional);
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString().Should().ContainAll("item.@Value", "item.@Optional");
    }

    [Fact(DisplayName = "Sorting generator preserves nullable projection annotations and guards root values")]
    public void Generate_PreservesNullableProjection()
    {
        var result = Generate("""
            public record Model(string Name, int Rank, Department? Department);
            public record Department(int Rank);
            public partial class Sorting : IReadModelSorting<Model?>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        var generated = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        generated.Should().ContainAll("IQueryable<global::Model?>", "IReadModelSorting<global::Model?>",
            "item == null ? default(int?) : item.@Rank", "item == null || item.@Department == null");
    }

    [Fact(DisplayName = "Sorting generator unwraps nullable value type projections and guards root values")]
    public void Generate_SupportsNullableValueTypeProjection()
    {
        var result = Generate("""
            public readonly record struct Model(int Rank, Department? Department);
            public readonly record struct Department(int Rank);
            public partial class Sorting : IReadModelSorting<Model?>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        var generated = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        generated.Should().ContainAll("IQueryable<global::Model?>", "IReadModelSorting<global::Model?>",
            "!item.HasValue ? default(int?) : item.Value.@Rank",
            "!item.HasValue || item.Value.@Department == null", "new global::Model(");
        generated.Should().NotContain("\"HasValue\"").And.NotContain("\"Value.Rank\"");
    }

    [Fact(DisplayName = "Sorting generator supports non-nullable value type projections")]
    public void Generate_SupportsValueTypeProjection()
    {
        var result = Generate("""
            public readonly record struct Model(int Rank);
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString()
            .Should().Contain("item.@Rank").And.NotContain("item == null");
    }

    [Fact(DisplayName = "Sorting generator maps positional constructors using typed expressions without runtime discovery")]
    public void Generate_MapsPositionalConstructors()
    {
        var result = Generate("""
            public record Base { public required int Field; }
            public record Model(string? Name, Detail? Detail) : Base
            {
                public required string Other { get; init; }
                public Model(int number) : this(number.ToString(), null) { }
            }
            public readonly record struct Detail(int Rank);
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        var generated = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        generated.Should().ContainAll("SortingProjectionRewriter", "new global::Model(", "new global::Detail(", "@Other = default!", "@Field = default!");
        generated.Should().NotContain("GetProperty").And.NotContain("GetParameters").And.NotContain("GetConstructor")
            .And.NotContain("System.Reflection").And.NotContain("MakeGenericMethod").And.NotContain(".Compile(");
        generated.Should().Contain("DynamicDependency(global::System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties");
    }

    [Theory(DisplayName = "Sorting generator does not map customized positional properties to constructor arguments")]
    [InlineData("record")]
    [InlineData("record struct")]
    public void Generate_SkipsCustomizedPositionalProperties(string kind)
    {
        var result = Generate($$"""
            public {{kind}} Model(int Rank)
            {
                public int Rank { get; init; } = -Rank;
            }
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString()
            .Should().Contain("item.@Rank").And.NotContain("SortingProjectionRewriter");
    }

    [Fact(DisplayName = "Sorting generator does not map inherited positional properties to derived constructor arguments")]
    public void Generate_SkipsInheritedPositionalProperties()
    {
        var result = Generate("""
            public record Base(int Rank);
            public record Model(int Rank) : Base(-Rank);
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);

        result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString()
            .Should().Contain("item.@Rank").And.NotContain("SortingProjectionRewriter");
    }

    [Theory(DisplayName = "Sorting generator reports unsupported sorting type declarations")]
    [InlineData("public class Sorting", "Model", "PANEFSG001")]
    [InlineData("public partial class Sorting<T>", "T", "PANEFSG002")]
    [InlineData("file partial class Sorting", "Model", "PANEFSG001")]
    public void Generate_ReportsInvalidSorting(string declaration, string projection, string diagnostic)
    {
        var result = Generate($$"""
            public record Model(string Name);
            {{declaration}} : IReadModelSorting<{{projection}}>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """, diagnostic);

        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact(DisplayName = "Sorting generator detects case-insensitive path collisions")]
    public void Generate_RejectsAmbiguousFields()
    {
        Generate("""
            public record Model(string Name, string name);
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """, "PANEFSG003");
    }

    [Fact(DisplayName = "Sorting generator permits a custom sorting implementation without partial")]
    public void Generate_PreservesCustomSorting()
    {
        var result = Generate("""
            public record Model(string Name);
            public class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
                public static IQueryable<Model> ApplySorting(IQueryable<Model> query, SortingParameters sortingParameters) => query;
            }
            """);

        result.GeneratedSources.Should().BeEmpty();
    }

    [Theory(DisplayName = "Sorting generator supports nested sorting classes and records")]
    [InlineData("class")]
    [InlineData("struct")]
    [InlineData("record class")]
    [InlineData("record struct")]
    public void Generate_SupportsNestedSortingTypes(string kind)
    {
        var result = Generate($$"""
            namespace Example;
            public record Model(int @event);
            public partial class Outer<T>
            {
                public partial {{kind}} Sorting : IReadModelSorting<Model>
                {
                    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
                }
            }
            """);

        result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString().Should().Contain("item.@event");
    }

    [Fact(DisplayName = "Sorting generator accepts a model without sortable fields")]
    public void Generate_SupportsEmptyModels()
    {
        Generate("""
            public record Model;
            public partial class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """);
    }

    [Theory(DisplayName = "Sorting generator reads referenced model paths without guessing constructor assignments")]
    [InlineData("")]
    [InlineData("public int Rank { get; init; } = -Rank;")]
    public void Generate_ReadsReferencedModels(string property)
    {
        var models = CSharpCompilation.Create("ExternalModels", [CSharpSyntaxTree.ParseText($$"""
            namespace External;
            public interface IBase { int Rank { get; } }
            public interface IDepartment : IBase { string Name { get; } }
            public record struct Detail(int Rank)
            {
                {{property}}
                public string WriteOnly { set { } }
                public Detail(string Rank) : this(int.Parse(Rank)) { }
                public Detail(string WriteOnly, int unused) : this(0) { }
            }
            public class Container<T>
            {
                public record Model(T Value, IDepartment Department, Detail Detail);
            }
            """, cancellationToken: TestContext.Current.CancellationToken)], References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        models.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();

        var result = Generate("""
            public partial class Sorting : IReadModelSorting<External.Container<int>.Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """, additionalReference: MetadataReference.CreateFromImage(stream.ToArray()));

        result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString().Should().ContainAll(
            "item.@Value", "item.@Department.@Rank", "item.@Department.@Name", "item.@Detail.@Rank");
        result.GeneratedSources[0].SourceText.ToString().Should().NotContain("SortingProjectionRewriter");
    }

    [Theory(DisplayName = "Sorting generator rejects open generic projections")]
    [InlineData("Model<T>")]
    [InlineData("Model<T[]>")]
    [InlineData("Container<T>.Model")]
    public void Generate_RejectsOpenGenericProjection(string projection)
    {
        Generate($$"""
            public record Model<T>(T Value);
            public class Container<T> { public record Model(T Value); }
            public partial class Sorting<T> : IReadModelSorting<{{projection}}>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
            }
            """, "PANEFSG002");
    }

    [Fact(DisplayName = "Sorting generator ignores abstract sorting types and unrelated contracts")]
    public void Generate_IgnoresUnrelatedTypes()
    {
        var result = Generate("""
            public interface IOther { }
            public class Other : IOther { }
            public interface IMore : IOther { }
            public record Model;
            public class DbModel : PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models.ReadDbModel<int> { }
            public class Mapper : IReadModelMapper<int, DbModel, Model>
            {
                public static IQueryable<Model> ProjectTo(IQueryable<DbModel> query) => throw new NotImplementedException();
            }
            public abstract class Sorting : IReadModelSorting<Model>
            {
                public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
                public static IQueryable<Model> ApplySorting(IQueryable<Model> query, SortingParameters sortingParameters) => query;
            }
            """);

        result.GeneratedSources.Should().BeEmpty();
    }

    [Fact(DisplayName = "Sorting generator ignores projects without the EF sorting type contract")]
    public void Generate_IgnoresProjectsWithoutEf()
    {
        var references = References.Where(reference => !reference.Display!.EndsWith("PANiXiDA.Core.Infrastructure.Persistence.Ef.dll", StringComparison.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create("NoEf", [CSharpSyntaxTree.ParseText("public class Model : object { }", cancellationToken: TestContext.Current.CancellationToken)], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new SortingGenerator()).RunGenerators(compilation, TestContext.Current.CancellationToken);

        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        driver.GetRunResult().Diagnostics.Should().BeEmpty();
    }

    [Fact(DisplayName = "Sorting generator reports multiple projection contracts on one sorting type")]
    public void Generate_RejectsMultipleContracts()
    {
        Generate("""
            public record Model;
            public record Other;
            public partial class Sorting : IReadModelSorting<Model>, IReadModelSorting<Other> { }
            """, "PANEFSG002");
    }

    [Fact(DisplayName = "Sorting requires the consumer to implement DefaultSorting")]
    public void Generate_RequiresDefaultSorting()
    {
        Generate("""
            public record Model(string Name);
            public partial class Sorting : IReadModelSorting<Model> { }
            """, expectedCompilationError: "CS0535");
    }

    [Fact(DisplayName = "Generated sorting supports explicitly implemented default criteria")]
    public void Generate_SupportsExplicitDefaults()
    {
        Generate("""
            public record Model(string Name);
            public partial class Sorting : IReadModelSorting<Model>
            {
                static SortingParameters IReadModelSorting<Model>.DefaultSorting { get; } = SortingParameters.Ascending(nameof(Model.Name));
            }
            """);
    }

    private static GeneratorRunResult Generate(string source, string? expectedDiagnostic = null,
        MetadataReference? additionalReference = null, string? expectedCompilationError = null)
    {
        var syntax = CSharpSyntaxTree.ParseText("""
            using System;
            using System.Linq;
            using System.Collections.Generic;
            using PANiXiDA.Core.Application.Querying.Sorting;
            using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Mapping;
            using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting;

            """ + source);
        var references = additionalReference is null ? References : References.Add(additionalReference);
        var compilation = CSharpCompilation.Create("SortingConsumer", [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new SortingGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        if (expectedDiagnostic is null)
        {
            diagnostics.Should().BeEmpty();
            var errors = output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            if (expectedCompilationError is null)
            {
                errors.Should().BeEmpty();
                output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Warning).Should().BeEmpty();
            }
            else
            {
                errors.Should().ContainSingle(diagnostic => diagnostic.Id == expectedCompilationError);
            }
        }
        else
        {
            diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == expectedDiagnostic);
        }

        return driver.GetRunResult().Results.Single();
    }
}
