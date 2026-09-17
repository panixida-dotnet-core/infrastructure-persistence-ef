using Microsoft.EntityFrameworkCore;
using PANiXiDA.Core.Application.Querying;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Mapping;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Sorting;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class GeneratedSortingTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "Generated sorting uses projected and calculated fields before database pagination")]
    public async Task Sorting_UsesProjectionBeforePagination()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var sortingParameters = SortingParameters.Descending("label");

        var result = await repository.GetProjectionPageAsync<ProductView, ProductViewMapper, ProductViewSorting>(new PaginationParameters(2, 1), sortingParameters);

        result.Items.Select(item => item.Rank).Should().Equal(2);
        result.TotalCount.Should().Be(4);
        result.TotalPages.Should().Be(4);
        var sql = ProductViewSorting.ApplySorting(ProductViewMapper.ProjectTo(repository.Products), sortingParameters).Skip(1).Take(1).ToQueryString();
        sql.Should().ContainAll("ORDER BY", "upper(", "DESC", "LIMIT", "OFFSET");
    }

    [Theory(DisplayName = "Generated sorting orders optional navigation fields in PostgreSQL")]
    [InlineData("department.name", "ALPHA,ALPHA,BETA,GAMMA")]
    [InlineData("Department.Rank", "BETA,ALPHA,ALPHA,GAMMA")]
    public async Task Sorting_UsesNestedFields(string field, string expected)
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var query = ProductViewSorting.ApplySorting(ProductViewMapper.ProjectTo(repository.Products), SortingParameters.Ascending(field));

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

        var result = ProductViewSorting.ApplySorting(query, SortingParameters.Descending("department.rank")).ToArray();

        result.Select(item => item.Label).Should().Equal("high", "low", "none");
    }

    [Fact(DisplayName = "Projection grouping counts projected rows and sorts aggregate values")]
    public async Task Sorting_HandlesGrouping()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProjectionPageAsync<SummaryView, SummaryMapper, SummaryViewSorting>(
            new PaginationParameters(1, 1), SortingParameters.Descending(nameof(SummaryView.Count)));

        result.TotalCount.Should().Be(3);
        result.Items.Should().ContainSingle().Which.Should().Be(new SummaryView { Label = "Alpha", Count = 2 });
    }

    [Fact(DisplayName = "Distinct projections preserve projected count and require no identifier")]
    public async Task Sorting_HandlesDistinctWithoutId()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProjectionPageAsync<LabelView, DistinctMapper, LabelViewSorting>(
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

        var action = () => ProductViewSorting.ApplySorting(query, SortingParameters.Of(new SortField(field, direction)));

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Generated sorting returns the original query when empty and validates null arguments")]
    public void Sorting_HandlesEmptyAndNullArguments()
    {
        var query = Array.Empty<ProductView>().AsQueryable();

        NoDefaultProductViewSorting.ApplySorting(query, SortingParameters.None).Should().BeSameAs(query);
        var missingQuery = () => ProductViewSorting.ApplySorting(null!, SortingParameters.None);
        var missingSorting = () => ProductViewSorting.ApplySorting(query, null!);
        var missingFields = () => ProductViewSorting.ApplySorting(query, new SortingParameters(null!));
        var missingField = () => ProductViewSorting.ApplySorting(query, new SortingParameters([null!]));

        missingQuery.Should().Throw<ArgumentNullException>();
        missingSorting.Should().Throw<ArgumentNullException>();
        missingFields.Should().Throw<ArgumentNullException>();
        missingField.Should().Throw<ArgumentNullException>();
    }

    [Fact(DisplayName = "Sorting class defaults apply to both a page and an unpaginated list")]
    public async Task Sorting_AppliesDefaultsToPageAndList()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var page = await repository.GetProjectionPageAsync<ProductView, ProductViewMapper, ProductViewSorting>(
            new PaginationParameters(2, 1), SortingParameters.None);
        var items = await repository.GetProjectionListAsync<ProductView, ProductViewMapper, ProductViewSorting>(SortingParameters.None);

        page.Items.Select(item => item.Rank).Should().Equal(3);
        items.Select(item => item.Rank).Should().Equal(1, 3, 2, 4);
    }

    [Fact(DisplayName = "Sorting appends defaults after client criteria without modifying either input")]
    public async Task Sorting_AppendsDefaultsWithoutMutatingInputs()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var sortingParameters = SortingParameters.Descending("department.rank");
        var defaults = ProductViewSorting.DefaultSorting.Fields.ToArray();

        var items = await repository.GetProjectionListAsync<ProductView, ProductViewMapper, ProductViewSorting>(sortingParameters);

        items.Select(item => item.Rank).Should().Equal(4, 1, 3, 2);
        sortingParameters.Fields.Should().Equal(new SortField("department.rank", SortDirection.Desc));
        ProductViewSorting.DefaultSorting.Fields.Should().Equal(defaults);
    }

    [Fact(DisplayName = "Sorting rejects null default parameters, arrays, and criteria")]
    public void Sorting_RejectsNullDefaults()
    {
        var query = Array.Empty<ProductView>().AsQueryable();

        var missingDefaults = () => NullDefaultSorting.ApplySorting(query, SortingParameters.None);
        var missingFields = () => NullDefaultFieldsSorting.ApplySorting(query, SortingParameters.None);
        var missingField = () => NullDefaultFieldSorting.ApplySorting(query, SortingParameters.None);

        missingDefaults.Should().Throw<ArgumentNullException>();
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

internal sealed class ProductViewMapper : IReadModelMapper<int, ProductReadDbModel, ProductView>
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

internal sealed class SummaryMapper : IReadModelMapper<int, ProductReadDbModel, SummaryView>
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

internal sealed class DistinctMapper : IReadModelMapper<int, ProductReadDbModel, LabelView>
{
    public static IQueryable<LabelView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new LabelView { Label = item.Name }).Distinct();
    }
}

internal sealed partial class ProductViewSorting : IReadModelSorting<ProductView>
{
    public static SortingParameters DefaultSorting { get; } =
        SortingParameters.Of(new SortField("LABEL"), new SortField(nameof(ProductView.Rank)));
}

internal sealed partial class NoDefaultProductViewSorting : IReadModelSorting<ProductView>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
}

internal sealed partial class SummaryViewSorting : IReadModelSorting<SummaryView>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.Ascending(nameof(SummaryView.Label));
}

internal sealed partial class LabelViewSorting : IReadModelSorting<LabelView>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.Ascending(nameof(LabelView.Label));
}

internal sealed partial class NullDefaultSorting : IReadModelSorting<ProductView>
{
    public static SortingParameters DefaultSorting { get; } = null!;
}

internal sealed partial class NullDefaultFieldsSorting : IReadModelSorting<ProductView>
{
    public static SortingParameters DefaultSorting { get; } = new(null!);
}

internal sealed partial class NullDefaultFieldSorting : IReadModelSorting<ProductView>
{
    public static SortingParameters DefaultSorting { get; } = new([null!]);
}
