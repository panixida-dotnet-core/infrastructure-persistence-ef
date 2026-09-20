using System.Collections.Immutable;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Registries;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Generators.Registries;

public sealed class RepositoryRegistrationGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
    ];

    [Fact(DisplayName = "Repository generator registers inherited non-generic contracts and deduplicates partial classes")]
    public void Generate_RegistersInheritedContracts()
    {
        var result = Generate("""
            public interface IParent : IReadRepository<int> { }
            public interface IChild : IParent { }
            public abstract class Base<T> : ReadBase<int>, IChild { }
            internal sealed partial class Repository : Base<int> { }
            internal sealed partial class Repository : Base<int> { }
            """);

        var generated = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        generated.Should().Contain("AddScoped<global::IChild, global::Repository>(services)");
        generated.Should().Contain("AddScoped<global::IParent, global::Repository>(services)");
        generated.Split("AddScoped<global::IChild, global::Repository>").Should().HaveCount(2);
        generated.Should().NotContain("GetTypes(").And.NotContain("GetInterfaces(").And.NotContain("Activator.");
    }

    [Fact(DisplayName = "Repository generator ignores abstract, open generic, direct generic and unrelated implementations")]
    public void Generate_IgnoresUnsupportedImplementations()
    {
        var result = Generate("""
            public interface IContract : IReadRepository<int> { }
            public interface IGeneric<T> : IReadRepository<T> where T : struct { }
            public abstract class AbstractRepository : ReadBase<int>, IContract { }
            public class GenericRepository<T> : ReadBase<int>, IContract { }
            public class Container<T> { public class Nested : ReadBase<int>, IContract { } }
            public class GenericContractRepository : ReadBase<int>, IGeneric<int> { }
            public class DirectRepository : ReadBase<int>, IReadRepository<int> { }
            public interface IUnrelated { }
            public class UnrelatedRepository : IUnrelated { }
            """);

        var generated = result.GeneratedSources.Should().ContainSingle().Subject.SourceText.ToString();
        generated.Should().Contain("RegisterAssembly(");
        generated.Should().NotContain("AddScoped<");
    }

    [Theory(DisplayName = "Repository generator diagnoses implementations inaccessible from generated code")]
    [InlineData("public class Container { private class Repository : ReadBase<int>, IContract { } }")]
    [InlineData("file class Repository : ReadBase<int>, IContract { }")]
    public void Generate_RejectsInaccessibleImplementations(string implementation)
    {
        Generate("public interface IContract : IReadRepository<int> { } " + implementation, "PANEFSG004");
    }

    [Fact(DisplayName = "Repository generator supports nested internal classes and escaped identifiers")]
    public void Generate_SupportsNestedTypesAndKeywords()
    {
        var result = Generate("""
            namespace @event
            {
                internal interface @interface : IReadRepository<int> { }
                internal class @class { internal class @new : ReadBase<int>, @interface { } }
            }
            """);

        result.GeneratedSources.Single().SourceText.ToString().Should()
            .Contain("AddScoped<global::@event.@interface, global::@event.@class.@new>(services)");
    }

    [Fact(DisplayName = "Repository generator preserves runtime duplicate registration checks")]
    public void Generate_PreservesConflictingImplementations()
    {
        var result = Generate("""
            public interface IContract : IReadRepository<int> { }
            public class BRepository : ReadBase<int>, IContract { }
            public class ARepository : ReadBase<int>, IContract { }
            """);

        var generated = result.GeneratedSources.Single().SourceText.ToString();
        generated.Should().Contain("AddScoped<global::IContract, global::ARepository>(services)")
            .And.Contain("AddScoped<global::IContract, global::BRepository>(services)")
            .And.Contain("EnsureNotRegistered(services, typeof(global::IContract), typeof(global::BRepository))");
        generated.IndexOf("typeof(global::ARepository)", StringComparison.Ordinal).Should()
            .BeLessThan(generated.IndexOf("typeof(global::BRepository)", StringComparison.Ordinal));
    }

    [Fact(DisplayName = "Repository generator includes a shared contract in both write and read registrations")]
    public void Generate_RegistersWriteAndReadContract()
    {
        var result = Generate("""
            public readonly record struct Id(System.Guid Value) : PANiXiDA.Core.Domain.Identifiers.IStronglyTypedId;
            public sealed class Aggregate(Id id) : PANiXiDA.Core.Domain.AggregateRoots.AggregateRoot<Id>(id) { }
            public interface IContract : PANiXiDA.Core.Domain.Abstractions.IRepository<Id, Aggregate>, IReadRepository<int> { }
            public sealed class Repository : ReadBase<int>, IContract
            {
                public Task<Aggregate?> GetByIdAsync(Id id, CancellationToken cancellationToken)
                {
                    return Task.FromResult<Aggregate?>(null);
                }

                public Task AddAsync(Aggregate aggregateRoot, CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }

                public Task UpdateAsync(Aggregate aggregateRoot, CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }

                public Task DeleteAsync(Aggregate aggregateRoot, CancellationToken cancellationToken)
                {
                    return Task.CompletedTask;
                }
            }
            """);

        var generated = result.GeneratedSources.Single().SourceText.ToString();
        generated.Split("AddScoped<global::IContract, global::Repository>(services)").Should().HaveCount(3);
    }

    [Fact(DisplayName = "Repository generator ignores projects without the runtime registry")]
    public void Generate_IgnoresProjectsWithoutEf()
    {
        var references = References.Where(reference => !reference.Display!.EndsWith("PANiXiDA.Core.Infrastructure.Persistence.Ef.dll", StringComparison.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create("NoEf", references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new RepositoryRegistrationGenerator()).RunGenerators(compilation, TestContext.Current.CancellationToken);

        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        driver.GetRunResult().Diagnostics.Should().BeEmpty();
    }

    [Fact(DisplayName = "Generated registration initializes a separately loaded assembly and resolves scoped repositories")]
    public void GeneratedRegistration_InitializesAssemblyAndResolvesScopedRepositories()
    {
        var (_, compilation) = Compile("""
            public interface IContract : IReadRepository<int> { }
            public class Dependency { }
            public class Repository(Dependency dependency) : ReadBase<int>, IContract
            {
                public Dependency Dependency { get; } = dependency;
            }
            """);
        using var stream = new MemoryStream();
        compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var services = new ServiceCollection();
            var dependency = assembly.GetType("Dependency", throwOnError: true)!;
            services.AddScoped(dependency);

            RepositoryRegistry.GetRegistration(assembly).RegisterReadRepositories(services);

            var descriptor = services.Should().ContainSingle(item => item.ServiceType.Name == "IContract").Subject;
            descriptor.Lifetime.Should().Be(ServiceLifetime.Scoped);
            using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
            using var firstScope = provider.CreateScope();
            using var secondScope = provider.CreateScope();
            var first = firstScope.ServiceProvider.GetRequiredService(descriptor.ServiceType);
            firstScope.ServiceProvider.GetRequiredService(descriptor.ServiceType).Should().BeSameAs(first);
            secondScope.ServiceProvider.GetRequiredService(descriptor.ServiceType).Should().NotBeSameAs(first);
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact(DisplayName = "Generated registration throws the original error when two implementations share a contract")]
    public void GeneratedRegistration_ThrowsForDuplicateImplementations()
    {
        var (_, compilation) = Compile("""
            public interface IContract : IReadRepository<int> { }
            public class ARepository : ReadBase<int>, IContract { }
            public class BRepository : ReadBase<int>, IContract { }
            """);
        using var stream = new MemoryStream();
        compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        stream.Position = 0;
        var loadContext = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            var assembly = loadContext.LoadFromStream(stream);
            var services = new ServiceCollection();

            var act = () => RepositoryRegistry.GetRegistration(assembly).RegisterReadRepositories(services);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("Repository interface 'IContract' is already registered. Conflicting implementation: 'BRepository'.");
        }
        finally
        {
            loadContext.Unload();
        }
    }

    [Fact(DisplayName = "Repository generator ignores identically named foreign contracts")]
    public void Generate_IgnoresForeignContract()
    {
        var foreign = CSharpCompilation.Create("ForeignContracts",
            [CSharpSyntaxTree.ParseText("namespace PANiXiDA.Core.Application.Persistence { public interface IReadRepository<T> { } }", cancellationToken: TestContext.Current.CancellationToken)],
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        foreign.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        var reference = MetadataReference.CreateFromImage(stream.ToArray(), new MetadataReferenceProperties(aliases: ["foreign"]));

        var (result, _) = Compile("""
            public interface IForeign : foreign::PANiXiDA.Core.Application.Persistence.IReadRepository<int> { }
            public class Repository : IForeign { }
            """, additionalReference: reference);

        result.GeneratedSources.Single().SourceText.ToString().Should().NotContain("AddScoped<");
    }

    private static GeneratorRunResult Generate(string source, string? expectedDiagnostic = null)
    {
        return Compile(source, expectedDiagnostic).Result;
    }

    private static (GeneratorRunResult Result, Compilation Compilation) Compile(string source, string? expectedDiagnostic = null,
        MetadataReference? additionalReference = null)
    {
        var prefix = additionalReference is null ? "" : "extern alias foreign;\n";
        var syntax = CSharpSyntaxTree.ParseText(prefix + """
            using System.Threading;
            using System.Threading.Tasks;
            using PANiXiDA.Core.Application.Persistence;
            public abstract class ReadBase<T> : IReadRepository<T> where T : struct
            {
                public Task<bool> ExistsByIdAsync(T id, CancellationToken cancellationToken)
                {
                    return Task.FromResult(false);
                }

                public Task<bool> AnyAsync(CancellationToken cancellationToken)
                {
                    return Task.FromResult(false);
                }
            }

            """ + source);
        var references = additionalReference is null ? References : References.Add(additionalReference);
        var compilation = CSharpCompilation.Create("RepositoryConsumer", [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new RepositoryRegistrationGenerator());

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
