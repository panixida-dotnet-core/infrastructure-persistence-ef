using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Extensions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.ReadModels;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests;

public sealed class ExtensionTests
{
    [Fact(DisplayName = "HasGenericBaseType returns true when type inherits from generic base type")]
    public void HasGenericBaseType_ReturnsTrue_WhenTypeInheritsFromGenericBaseType()
    {
        var type = typeof(ProductAuditableReadDbModel);

        var result = type.HasGenericBaseType(typeof(ProductAuditableReadDbModel).BaseType!.GetGenericTypeDefinition());

        result.Should().BeTrue();
    }

    [Fact(DisplayName = "HasGenericBaseType returns false when generic base type is not found")]
    public void HasGenericBaseType_ReturnsFalse_WhenGenericBaseTypeIsNotFound()
    {
        var type = typeof(string);

        var result = type.HasGenericBaseType(typeof(ProductAuditableReadDbModel).BaseType!.GetGenericTypeDefinition());

        result.Should().BeFalse();
    }

    [Fact(DisplayName = "ToSchemaName trims the first matching suffix and converts name to snake case")]
    public void ToSchemaName_TrimsFirstMatchingSuffixAndConvertsNameToSnakeCase()
    {
        var result = typeof(SchemaWriteDbContext).ToSchemaName(
            nameof(WriteDbContext<>),
            nameof(DbContext));

        result.Should().Be("schema");
    }

    [Fact(DisplayName = "ToTableName trims model suffix and pluralizes table name")]
    public void ToTableName_TrimsModelSuffixAndPluralizesTableName()
    {
        var result = typeof(ProductReadDbModel).ToTableName(nameof(ReadDbModel<>));

        result.Should().Be("products");
    }

    [Fact(DisplayName = "ToPluralTableName converts singular name to plural snake case")]
    public void ToPluralTableName_ConvertsSingularNameToPluralSnakeCase()
    {
        var result = "Category".ToPluralTableName();

        result.Should().Be("categories");
    }

    [Fact(DisplayName = "TrimFirstMatchingSuffix uses longest non-empty suffix")]
    public void TrimFirstMatchingSuffix_UsesLongestNonEmptySuffix()
    {
        var result = "OrderReadDbModel".TrimFirstMatchingSuffix(
            string.Empty,
            "DbModel",
            "ReadDbModel");

        result.Should().Be("Order");
    }

    [Fact(DisplayName = "TrimFirstMatchingSuffix returns original value when no suffix matches")]
    public void TrimFirstMatchingSuffix_ReturnsOriginalValue_WhenNoSuffixMatches()
    {
        var result = "Order".TrimFirstMatchingSuffix("ReadDbModel");

        result.Should().Be("Order");
    }
}
