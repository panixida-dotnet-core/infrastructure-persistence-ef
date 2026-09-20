using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Registries;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.ReadModels;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class ReadDbModelRegistryTests
{
    [Fact(DisplayName = "Read model registry rejects a null assembly")]
    public void RegisterAssembly_RejectsNullAssembly()
    {
        var act = () => ReadDbModelRegistry.RegisterAssembly(null!, (_, _, _) => { });

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact(DisplayName = "Read model registry rejects a null callback")]
    public void RegisterAssembly_RejectsNullCallback()
    {
        var act = () => ReadDbModelRegistry.RegisterAssembly(typeof(ReadDbModelRegistryTests).Assembly, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("registerReadDbModels");
    }

    [Fact(DisplayName = "Read model registry does not overwrite an existing registration")]
    public void RegisterAssembly_RejectsDuplicateAssembly()
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.RunAndCollect);
        ReadDbModelRegistry.RegisterAssembly(assembly, (_, _, _) => { });

        var act = () => ReadDbModelRegistry.RegisterAssembly(assembly, (_, _, _) => { });

        act.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Read model registration rejects a null assembly")]
    public void GetRegistration_RejectsNullAssembly()
    {
        var act = () => ReadDbModelRegistry.GetRegistration(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("assembly");
    }

    [Fact(DisplayName = "Read model registration reports a missing generator")]
    public void GetRegistration_ThrowsWhenGeneratorIsMissing()
    {
        var act = () => ReadDbModelRegistry.GetRegistration(typeof(string).Assembly);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("Generated read model registration was not found*with its analyzers*rebuild it.");
    }

    [Theory(DisplayName = "Generated read model registration preserves table names, schemas and migration settings")]
    [InlineData(null, true)]
    [InlineData(null, false)]
    [InlineData("reporting", true)]
    [InlineData("reporting", false)]
    public void GetRegistration_ConfiguresModels(string? schemaName, bool excludeFromMigrations)
    {
        var modelBuilder = new ModelBuilder();

        ReadDbModelRegistry.GetRegistration(typeof(ReadDbModelRegistryTests).Assembly)(modelBuilder, schemaName, excludeFromMigrations);

        var product = modelBuilder.Model.FindEntityType(typeof(ProductReadDbModel))!;
        product.GetTableName().Should().Be("products");
        product.GetSchema().Should().Be(schemaName);
        product.IsTableExcludedFromMigrations().Should().Be(excludeFromMigrations);
        product.GetDeclaredQueryFilters().Should().BeEmpty();
        var auditable = modelBuilder.Model.FindEntityType(typeof(ProductAuditableReadDbModel))!;
        auditable.GetTableName().Should().Be("product_auditables");
        var filter = auditable.GetDeclaredQueryFilters().Should().ContainSingle().Subject.Expression;
        var predicate = filter.Should().BeAssignableTo<Expression<Func<ProductAuditableReadDbModel, bool>>>().Subject.Compile(preferInterpretation: true);
        predicate(new ProductAuditableReadDbModel()).Should().BeTrue();
        predicate(new ProductAuditableReadDbModel { DeletedAt = DateTime.UtcNow }).Should().BeFalse();
    }
}
