using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Registries;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Generators.Registries;

public sealed class ReadDbModelRegistrationGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
    ];

    [Fact(DisplayName = "Read model generator supports inherited models and deduplicates partial declarations")]
    public void Generate_RegistersInheritedModelsOnce()
    {
        var (result, _) = Compile("""
            public abstract class Base<T> : AuditableReadDbModel<T> where T : struct { }
            public abstract class Middle : Base<int> { }
            internal sealed partial class OrderReadDbModel : Middle { }
            internal sealed partial class OrderReadDbModel : Middle { }
            public class Category : ReadDbModel<int> { }
            public class Unrelated { }
            """);

        var source = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        source.Split("Entity<global::OrderReadDbModel>()").Should().HaveCount(2);
        source.Should().Contain("Entity<global::Category>()")
            .And.Contain("\"Order\".Pluralize(inputIsKnownToBeSingular: false)")
            .And.Contain("entity.HasQueryFilter(item => item.DeletedAt == null)")
            .And.NotContain("Entity<global::Middle>")
            .And.NotContain("Entity<global::Unrelated>")
            .And.NotContain("GetTypes(")
            .And.NotContain("Expression.Property(")
            .And.NotContain("MakeGenericType(");
        source.IndexOf("Entity<global::Category>", StringComparison.Ordinal).Should()
            .BeLessThan(source.IndexOf("Entity<global::OrderReadDbModel>", StringComparison.Ordinal));
    }

    [Theory(DisplayName = "Read model generator diagnoses inaccessible models")]
    [InlineData("public class Container { private class Model : ReadDbModel<int> { } }")]
    [InlineData("public class Container { protected class Model : ReadDbModel<int> { } }")]
    [InlineData("file class Model : ReadDbModel<int> { }")]
    [InlineData("file class Container { public class Model : ReadDbModel<int> { } }")]
    public void Generate_RejectsInaccessibleModels(string source)
    {
        Compile(source, "PANEFSG005");
    }

    [Theory(DisplayName = "Read model generator diagnoses open generic models")]
    [InlineData("public class Model<T> : ReadDbModel<int> { }")]
    [InlineData("public class Container<T> { public class Model : ReadDbModel<int> { } }")]
    public void Generate_RejectsOpenGenericModels(string source)
    {
        Compile(source, "PANEFSG006");
    }

    [Fact(DisplayName = "Read model generator supports nested internal types and escaped identifiers")]
    public void Generate_SupportsNestedTypesAndKeywords()
    {
        var (result, _) = Compile("""
            namespace @event
            {
                internal class @class { internal sealed class @new : ReadDbModel<int> { } }
            }
            """);

        result.GeneratedSources.Single().SourceText.ToString().Should()
            .Contain("Entity<global::@event.@class.@new>()")
            .And.Contain("\"new\".Pluralize(");
    }

    [Fact(DisplayName = "Read model generator ignores projects without the runtime registry")]
    public void Generate_IgnoresProjectsWithoutEf()
    {
        var references = References.Where(reference => !reference.Display!.EndsWith("PANiXiDA.Core.Infrastructure.Persistence.Ef.dll", StringComparison.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create("NoEf", references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new ReadDbModelRegistrationGenerator()).RunGenerators(compilation, TestContext.Current.CancellationToken);

        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        driver.GetRunResult().Diagnostics.Should().BeEmpty();
    }

    [Fact(DisplayName = "Read model generator compares base symbols rather than their names")]
    public void Generate_IgnoresForeignModelBases()
    {
        var foreign = CSharpCompilation.Create("ForeignModels",
            [CSharpSyntaxTree.ParseText("""
                namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models
                {
                    public class ReadDbModel<T> { }
                    public class AuditableReadDbModel<T> { }
                }
                """, cancellationToken: TestContext.Current.CancellationToken)],
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        foreign.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        var reference = MetadataReference.CreateFromImage(stream.ToArray(), new MetadataReferenceProperties(aliases: ["foreign"]));

        var (result, _) = Compile("""
            public class Foreign : foreign::PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models.ReadDbModel<int> { }
            public class ForeignAuditable : foreign::PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models.AuditableReadDbModel<int> { }
            """, additionalReference: reference);

        result.GeneratedSources.Single().SourceText.ToString().Should().NotContain("modelBuilder.Entity<");
    }

    [Fact(DisplayName = "Generated read model registration initializes an assembly before its code has executed")]
    public void GeneratedRegistration_InitializesAssemblyAndMapsOnlyItsModels()
    {
        var (_, compilation) = Compile("""
            public class PersonReadDbModel : ReadDbModel<int> { }
            public class AlreadyPluralPeople : ReadDbModel<int> { }
            public abstract class AuditBase<T> : AuditableReadDbModel<T> where T : struct { }
            public sealed class EntryReadDbModel : AuditBase<int> { }
            """);
        using var stream = new MemoryStream();
        compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var modelBuilder = new ModelBuilder();

            ReadDbModelRegistry.GetRegistration(assembly)(modelBuilder, "reporting", true);

            var models = modelBuilder.Model.GetEntityTypes().ToArray();
            models.Should().HaveCount(3);
            models.Should().OnlyContain(model => model.ClrType.Assembly == assembly);
            var person = models.Single(model => model.ClrType.Name == "PersonReadDbModel");
            person.GetTableName().Should().Be("people");
            person.GetSchema().Should().Be("reporting");
            person.IsTableExcludedFromMigrations().Should().BeTrue();
            person.GetDeclaredQueryFilters().Should().BeEmpty();
            models.Single(model => model.ClrType.Name == "AlreadyPluralPeople").GetTableName().Should().Be("already_plural_people");
            models.Single(model => model.ClrType.Name == "EntryReadDbModel").GetDeclaredQueryFilters().Should().ContainSingle();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact(DisplayName = "Generated read model registration handles an assembly without models")]
    public void GeneratedRegistration_HandlesEmptyAssembly()
    {
        var (_, compilation) = Compile("public class Unrelated { }");
        using var stream = new MemoryStream();
        compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var modelBuilder = new ModelBuilder();

            ReadDbModelRegistry.GetRegistration(assembly)(modelBuilder, null, false);

            modelBuilder.Model.GetEntityTypes().Should().BeEmpty();
        }
        finally
        {
            loadContext.Unload();
        }
    }

    private static (GeneratorRunResult Result, Compilation Compilation) Compile(string source, string? expectedDiagnostic = null,
        MetadataReference? additionalReference = null)
    {
        var prefix = additionalReference is null ? "" : "extern alias foreign;\n";
        var syntax = CSharpSyntaxTree.ParseText(prefix + "using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;\n" + source);
        var references = additionalReference is null ? References : References.Add(additionalReference);
        var compilation = CSharpCompilation.Create("ReadModelConsumer", [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS1702"] = ReportDiagnostic.Suppress }));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new ReadDbModelRegistrationGenerator());

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);

        if (expectedDiagnostic is null)
        {
            diagnostics.Should().BeEmpty();
            output.GetDiagnostics().Where(diagnostic => diagnostic.Severity is DiagnosticSeverity.Error or DiagnosticSeverity.Warning).Should().BeEmpty();
        }
        else
        {
            diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == expectedDiagnostic);
        }

        return (driver.GetRunResult().Results.Single(), output);
    }
}
