using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Generators.Registries;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Generators.Registries;

public sealed class EntityConfigurationRegistrationGeneratorTests
{
    private static readonly ImmutableArray<MetadataReference> References =
    [
        .. ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path))
    ];

    [Fact(DisplayName = "Entity configuration generator preserves inherited audit configuration and non-public constructors")]
    public void Generate_AppliesInheritedAuditAndPrivateConstructorConfigurations()
    {
        var (result, compilation) = Compile("""
            public class Order { public int Id { get; set; } }
            public class Other { }
            public abstract class Base<T> : AuditableEntityConfiguration<T> where T : class
            {
                protected override void ConfigureEntity(EntityTypeBuilder<T> builder) { builder.ToTable("orders"); }
                protected override void ConfigureAudit(EntityTypeBuilder<T> builder)
                {
                    base.ConfigureAudit(builder);
                    builder.Property<string>("Actor");
                }
            }
            internal sealed partial class OrderConfiguration : Base<Order> { }
            internal sealed partial class OrderConfiguration : Base<Order> { }
            internal sealed class PrivateConfiguration : IEntityTypeConfiguration<Other>
            {
                private PrivateConfiguration() { }
                void IEntityTypeConfiguration<Other>.Configure(EntityTypeBuilder<Other> builder) { builder.ToTable("others"); }
            }
            """);

        var source = result.GeneratedSources.Single().SourceText.ToString();
        source.Split("new global::OrderConfiguration()").Should().HaveCount(2);
        source.Should().Contain("ApplyConfiguration<global::Order>")
            .And.Contain("UnsafeAccessorKind.Constructor")
            .And.NotContain("Activator.").And.NotContain("MakeGenericMethod").And.NotContain("GetTypes(");
        WithAssembly(compilation, assembly =>
        {
            var builder = new ModelBuilder();
            EntityConfigurationRegistry.GetRegistration(assembly)(builder);

            var order = builder.Model.GetEntityTypes().Single(entity => entity.ClrType.Name == "Order");
            order.GetTableName().Should().Be("orders");
            order.FindProperty("CreatedAt")!.IsNullable.Should().BeFalse();
            order.FindProperty("CreatedAt")!.GetColumnOrder().Should().Be(1);
            order.FindProperty("UpdatedAt")!.GetColumnOrder().Should().Be(2);
            order.FindProperty("DeletedAt")!.GetColumnOrder().Should().Be(3);
            order.FindProperty("Actor").Should().NotBeNull();
            order.GetDeclaredQueryFilters().Should().ContainSingle();
            builder.Model.GetEntityTypes().Single(entity => entity.ClrType.Name == "Other").GetTableName().Should().Be("others");
            builder.Model.GetEntityTypes().Should().OnlyContain(entity => entity.ClrType.Assembly == assembly);
        });
    }

    [Fact(DisplayName = "Entity configuration generator preserves disabled soft delete and configuration order")]
    public void Generate_PreservesOverridesAndOrder()
    {
        var (_, compilation) = Compile("""
            public class Entity { }
            public class AConfiguration : AuditableEntityConfiguration<Entity>
            {
                protected override void ConfigureEntity(EntityTypeBuilder<Entity> builder) { builder.ToTable("first"); }
                protected override bool IsSoftDeleteEnabled() { return false; }
            }
            public class ZConfiguration : IEntityTypeConfiguration<Entity>
            {
                public void Configure(EntityTypeBuilder<Entity> builder) { builder.ToTable("last"); }
            }
            """);

        WithAssembly(compilation, assembly =>
        {
            var builder = new ModelBuilder();
            EntityConfigurationRegistry.GetRegistration(assembly)(builder);
            var entity = builder.Model.GetEntityTypes().Single();

            entity.GetTableName().Should().Be("last");
            entity.GetDeclaredQueryFilters().Should().BeEmpty();
            entity.FindProperty("CreatedAt").Should().NotBeNull();
        });
    }

    [Fact(DisplayName = "Entity configuration generator constructs a separate instance for each implemented contract")]
    public void Generate_AppliesEveryContractWithSeparateInstances()
    {
        var (_, compilation) = Compile("""
            public class First { }
            public class Second { }
            public class Configuration : IEntityTypeConfiguration<First>, IEntityTypeConfiguration<Second>
            {
                private int calls;
                public void Configure(EntityTypeBuilder<First> builder) { builder.HasAnnotation("Calls", ++calls); }
                public void Configure(EntityTypeBuilder<Second> builder) { builder.HasAnnotation("Calls", ++calls); }
            }
            """);

        WithAssembly(compilation, assembly =>
        {
            var builder = new ModelBuilder();
            EntityConfigurationRegistry.GetRegistration(assembly)(builder);

            builder.Model.GetEntityTypes().Should().HaveCount(2)
                .And.OnlyContain(entity => Equals(entity.FindAnnotation("Calls")!.Value, 1));
        });
    }

    [Fact(DisplayName = "Entity configuration generator matches reflection discovery for constructor and generic shapes")]
    public void Generate_MatchesExistingDiscovery()
    {
        var (_, compilation) = Compile("""
            public class Entity { }
            public interface IMarker { }
            public interface IChild : IMarker { }
            public struct Unrelated : IMarker { }
            public abstract class Abstract : IEntityTypeConfiguration<Entity>
            {
                public virtual void Configure(EntityTypeBuilder<Entity> builder) { throw new Exception(); }
            }
            public class Open<T> : Abstract { }
            public class Container<T> { public class Nested : Abstract { } }
            public class WithoutConstructor : Abstract { public WithoutConstructor(int value) { } }
            public struct ImplicitConstructor : IEntityTypeConfiguration<Entity>
            {
                public void Configure(EntityTypeBuilder<Entity> builder) { throw new Exception(); }
            }
            public struct ExplicitConstructor : IEntityTypeConfiguration<Entity>
            {
                public ExplicitConstructor() { }
                public void Configure(EntityTypeBuilder<Entity> builder) { builder.HasAnnotation("Struct", true); }
            }
            public abstract class RequiredBase { public required string Value { get; init; } }
            public sealed class RequiredConfiguration : RequiredBase, IEntityTypeConfiguration<Entity>
            {
                public void Configure(EntityTypeBuilder<Entity> builder) { builder.HasAnnotation("Required", Value is null); }
            }
            public class ProtectedConfiguration : IEntityTypeConfiguration<Entity>
            {
                protected ProtectedConfiguration() { }
                public void Configure(EntityTypeBuilder<Entity> builder) { builder.HasAnnotation("Protected", true); }
            }
            """);

        WithAssembly(compilation, assembly =>
        {
            var generated = new ModelBuilder();
            var reflected = new ModelBuilder();
            EntityConfigurationRegistry.GetRegistration(assembly)(generated);
            reflected.ApplyConfigurationsFromAssembly(assembly);

            var entity = generated.Model.GetEntityTypes().Single();
            entity.GetAnnotations().Select(annotation => (annotation.Name, annotation.Value)).Should()
                .BeEquivalentTo(reflected.Model.GetEntityTypes().Single().GetAnnotations().Select(annotation => (annotation.Name, annotation.Value)));
            entity.FindAnnotation("Struct")!.Value.Should().Be(true);
            entity.FindAnnotation("Required")!.Value.Should().Be(true);
            entity.FindAnnotation("Protected")!.Value.Should().Be(true);
        });
    }

    [Theory(DisplayName = "Entity configuration generator preserves constructor and configuration exceptions")]
    [InlineData("public Configuration() { throw new InvalidOperationException(\"failure\"); }", "")]
    [InlineData("private Configuration() { throw new InvalidOperationException(\"failure\"); }", "")]
    [InlineData("", "throw new InvalidOperationException(\"failure\");")]
    public void Generate_PreservesExceptionWrapping(string constructor, string configure)
    {
        var (_, compilation) = Compile($$"""
            public class Entity { }
            public class Configuration : IEntityTypeConfiguration<Entity>
            {
                {{constructor}}
                public void Configure(EntityTypeBuilder<Entity> builder) { {{configure}} }
            }
            """);

        WithAssembly(compilation, assembly =>
        {
            var generated = () => EntityConfigurationRegistry.GetRegistration(assembly)(new ModelBuilder());
            var reflected = () => new ModelBuilder().ApplyConfigurationsFromAssembly(assembly);

            generated.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>().WithMessage("failure");
            reflected.Should().Throw<TargetInvocationException>().WithInnerException<InvalidOperationException>().WithMessage("failure");
        });
    }

    [Theory(DisplayName = "Entity configuration generator diagnoses inaccessible configuration types")]
    [InlineData("public class Container { private class Configuration : Base { } }")]
    [InlineData("public class Container { protected class Configuration : Base { } }")]
    [InlineData("file class Configuration : Base { }")]
    [InlineData("file class Container { public class Configuration : Base { } }")]
    public void Generate_DiagnosesInaccessibleTypes(string source)
    {
        Compile("""
            public class Entity { }
            public abstract class Base : IEntityTypeConfiguration<Entity>
            {
                public void Configure(EntityTypeBuilder<Entity> builder) { }
            }
            """ + source, "PANEFSG007");
    }

    [Fact(DisplayName = "Entity configuration generator supports escaped names and empty assemblies")]
    public void Generate_SupportsNestedNamesAndEmptyAssemblies()
    {
        var (result, _) = Compile("""
            namespace @event
            {
                public class @class { }
                internal class Container
                {
                    internal class @new : IEntityTypeConfiguration<@class>
                    {
                        internal @new() { }
                        public void Configure(EntityTypeBuilder<@class> builder) { }
                    }
                }
            }
            """);
        result.GeneratedSources.Single().SourceText.ToString().Should()
            .Contain("ApplyConfiguration<global::@event.@class>(new global::@event.Container.@new())");

        var (_, compilation) = Compile("public class Unrelated { }");
        WithAssembly(compilation, assembly =>
        {
            var builder = new ModelBuilder();
            EntityConfigurationRegistry.GetRegistration(assembly)(builder);
            builder.Model.GetEntityTypes().Should().BeEmpty();
        });
    }

    [Fact(DisplayName = "Entity configuration generator ignores projects without the runtime registry")]
    public void Generate_IgnoresProjectsWithoutEf()
    {
        var references = References.Where(reference => !reference.Display!.EndsWith("PANiXiDA.Core.Infrastructure.Persistence.Ef.dll", StringComparison.OrdinalIgnoreCase));
        var compilation = CSharpCompilation.Create("NoEf", references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new EntityConfigurationRegistrationGenerator()).RunGenerators(compilation, TestContext.Current.CancellationToken);

        driver.GetRunResult().GeneratedTrees.Should().BeEmpty();
        driver.GetRunResult().Diagnostics.Should().BeEmpty();
    }

    [Fact(DisplayName = "Entity configuration generator compares contract symbols rather than their names")]
    public void Generate_IgnoresForeignContracts()
    {
        var foreign = CSharpCompilation.Create("ForeignContracts",
            [CSharpSyntaxTree.ParseText("namespace Microsoft.EntityFrameworkCore { public interface IEntityTypeConfiguration<T> { } }", cancellationToken: TestContext.Current.CancellationToken)],
            References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        foreign.Emit(stream, cancellationToken: TestContext.Current.CancellationToken).Success.Should().BeTrue();
        var reference = MetadataReference.CreateFromImage(stream.ToArray(), new MetadataReferenceProperties(aliases: ["foreign"]));

        var (result, _) = Compile("public class Foreign : foreign::Microsoft.EntityFrameworkCore.IEntityTypeConfiguration<object> { }", additionalReference: reference);

        result.GeneratedSources.Single().SourceText.ToString().Should().NotContain("modelBuilder.ApplyConfiguration<");
    }

    private static void WithAssembly(Compilation compilation, Action<Assembly> action)
    {
        using var stream = new MemoryStream();
        var emitted = compilation.Emit(stream, cancellationToken: TestContext.Current.CancellationToken);
        emitted.Success.Should().BeTrue(string.Join(Environment.NewLine, emitted.Diagnostics));
        stream.Position = 0;
        var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
        try
        {
            action(context.LoadFromStream(stream));
        }
        finally
        {
            context.Unload();
        }
    }

    private static (GeneratorRunResult Result, Compilation Compilation) Compile(string source, string? expectedDiagnostic = null,
        MetadataReference? additionalReference = null)
    {
        var prefix = additionalReference is null ? "" : "extern alias foreign;\n";
        var syntax = CSharpSyntaxTree.ParseText(prefix + """
            using System;
            using Microsoft.EntityFrameworkCore;
            using Microsoft.EntityFrameworkCore.Metadata.Builders;
            using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

            """ + source);
        var references = additionalReference is null ? References : References.Add(additionalReference);
        var compilation = CSharpCompilation.Create("ConfigurationConsumer", [syntax], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable,
                specificDiagnosticOptions: new Dictionary<string, ReportDiagnostic> { ["CS1702"] = ReportDiagnostic.Suppress }));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(new EntityConfigurationRegistrationGenerator());

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
