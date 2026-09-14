using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Application.Querying;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class GeneratedSortingTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "Generated sorting uses projected and calculated fields before database pagination")]
    public async Task Sorting_UsesProjectionBeforePagination()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var sorting = SortingParameters.Descending("label")
            .WithDefault(SortingParameters.Of(new SortField("LABEL"), new SortField(nameof(ProductView.Rank))));

        var result = await repository.GetProjectionPageAsync<ProductView, ProductViewMapper>(new PaginationParameters(2, 1), sorting);

        result.Items.Select(item => item.Rank).Should().Equal(2);
        result.TotalCount.Should().Be(4);
        result.TotalPages.Should().Be(4);
        var sql = ProductViewMapper.ApplySorting(ProductViewMapper.ProjectTo(repository.Products), sorting).Skip(1).Take(1).ToQueryString();
        sql.Should().ContainAll("ORDER BY", "upper(", "DESC", "LIMIT", "OFFSET");
    }

    [Theory(DisplayName = "Generated sorting orders optional navigation fields in PostgreSQL")]
    [InlineData("department.name", "ALPHA,ALPHA,BETA,GAMMA")]
    [InlineData("Department.Rank", "BETA,ALPHA,ALPHA,GAMMA")]
    public async Task Sorting_UsesNestedFields(string field, string expected)
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var query = ProductViewMapper.ApplySorting(ProductViewMapper.ProjectTo(repository.Products), SortingParameters.Ascending(field));

        var items = await query.ToListAsync(TestContext.Current.CancellationToken);

        items.Select(item => item.Label).Should().Equal(expected.Split(','));
        query.ToQueryString().Should().ContainAll("LEFT JOIN", "ORDER BY");
    }

    [Fact(DisplayName = "Generated sorting handles nullable nested objects without changing key types")]
    public void Sorting_HandlesNullNestedValues()
    {
        var query = new[]
        {
            new ProductView { Label = "none" },
            new ProductView { Label = "low", Department = new DepartmentView { Rank = 1 } },
            new ProductView { Label = "high", Department = new DepartmentView { Rank = 2 } }
        }.AsQueryable();

        var result = ProductViewMapper.ApplySorting(query, SortingParameters.Descending("department.rank")).ToArray();

        result.Select(item => item.Label).Should().Equal("high", "low", "none");
    }

    [Fact(DisplayName = "Projection grouping counts projected rows and sorts aggregate values")]
    public async Task Sorting_HandlesGrouping()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProjectionPageAsync<SummaryView, SummaryMapper>(
            new PaginationParameters(1, 1), SortingParameters.Descending(nameof(SummaryView.Count)));

        result.TotalCount.Should().Be(3);
        result.Items.Should().ContainSingle().Which.Should().Be(new SummaryView { Label = "Alpha", Count = 2 });
    }

    [Fact(DisplayName = "Distinct projections preserve projected count and require no identifier")]
    public async Task Sorting_HandlesDistinctWithoutId()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProjectionPageAsync<LabelView, DistinctMapper>(
            new PaginationParameters(2, 2), SortingParameters.Ascending(nameof(LabelView.Label)));

        result.TotalCount.Should().Be(3);
        result.Items.Should().ContainSingle().Which.Label.Should().Be("Gamma");
    }

    [Theory(DisplayName = "Generated sorting rejects unsupported fields and directions")]
    [InlineData("Score", SortDirection.Asc)]
    [InlineData("unsupported", SortDirection.Asc)]
    [InlineData("label:desc", SortDirection.Asc)]
    [InlineData("Label", (SortDirection)42)]
    public void Sorting_RejectsUnsupportedCriteria(string field, SortDirection direction)
    {
        var query = Array.Empty<ProductView>().AsQueryable();

        var action = () => ProductViewMapper.ApplySorting(query, SortingParameters.Of(new SortField(field, direction)));

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Generated sorting returns the original query when empty and validates null arguments")]
    public void Sorting_HandlesEmptyAndNullArguments()
    {
        var query = Array.Empty<ProductView>().AsQueryable();

        ProductViewMapper.ApplySorting(query, SortingParameters.None).Should().BeSameAs(query);
        var missingQuery = () => ProductViewMapper.ApplySorting(null!, SortingParameters.None);
        var missingSorting = () => ProductViewMapper.ApplySorting(query, null!);
        var missingFields = () => ProductViewMapper.ApplySorting(query, new SortingParameters(null!));
        var missingField = () => ProductViewMapper.ApplySorting(query, new SortingParameters([null!]));

        missingQuery.Should().Throw<ArgumentNullException>();
        missingSorting.Should().Throw<ArgumentNullException>();
        missingFields.Should().Throw<ArgumentNullException>();
        missingField.Should().Throw<ArgumentNullException>();
    }

    private async Task<WritableReadDbContext> CreateContextAsync()
    {
        var context = await fixture.CreateInitializedDbContextAsync<WritableReadDbContext>(options => new WritableReadDbContext(options));
        var first = new DepartmentReadDbModel { Id = 1, Name = "A", Rank = 2 };
        var second = new DepartmentReadDbModel { Id = 2, Name = "Z", Rank = 1 };
        context.AddRange(
            new ProductReadDbModel { Id = 1, Name = "Alpha", Score = 30, Department = first },
            new ProductReadDbModel { Id = 2, Name = "Beta", Score = 20, Department = second },
            new ProductReadDbModel { Id = 3, Name = "Alpha", Score = 10, Department = first },
            new ProductReadDbModel { Id = 4, Name = "Gamma", Score = 40 });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        return context;
    }
}

internal sealed record ProductView : IReadModel
{
    public required string Label { get; init; }
    public int Rank { get; init; }
    public DepartmentView? Department { get; init; }
}

internal sealed record DepartmentView
{
    public string Name { get; init; } = "";
    public int Rank { get; init; }
}

internal sealed partial class ProductViewMapper : IReadModelMapper<int, ProductReadDbModel, ProductView>
{
    public static IQueryable<ProductView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new ProductView
        {
            Label = item.Name.ToUpper(),
            Rank = item.Score / 10,
            Department = item.Department == null ? null : new DepartmentView { Name = item.Department.Name, Rank = item.Department.Rank }
        });
    }
}

internal sealed record SummaryView : IReadModel
{
    public required string Label { get; init; }
    public int Count { get; init; }
}

internal sealed partial class SummaryMapper : IReadModelMapper<int, ProductReadDbModel, SummaryView>
{
    public static IQueryable<SummaryView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.GroupBy(item => item.Name).Select(group => new SummaryView { Label = group.Key, Count = group.Count() });
    }
}

internal sealed record LabelView : IReadModel
{
    public required string Label { get; init; }
}

internal sealed partial class DistinctMapper : IReadModelMapper<int, ProductReadDbModel, LabelView>
{
    public static IQueryable<LabelView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new LabelView { Label = item.Name }).Distinct();
    }
}
