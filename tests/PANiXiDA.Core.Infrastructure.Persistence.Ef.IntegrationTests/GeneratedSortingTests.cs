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
    [Theory(DisplayName = "Generated sorting translates positional records and nested constructors in PostgreSQL")]
    [InlineData("label", SortDirection.Desc, "4,2,1,3")]
    [InlineData("department.name", SortDirection.Asc, "1,3,2,4")]
    [InlineData("Department.Rank", SortDirection.Desc, "4,1,3,2")]
    public async Task Sorting_UsesPositionalProjection(string field, SortDirection direction, string expected)
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var sortingParameters = SortingParameters.Of(new SortField(field, direction));
        var query = PositionalProductSorting.ApplySorting(PositionalProductMapper.ProjectTo(repository.Products), sortingParameters);

        var items = await query.ToListAsync(TestContext.Current.CancellationToken);
        var page = await repository.GetProjectionPageAsync<PositionalProductView, PositionalProductMapper, PositionalProductSorting>(
            new PaginationParameters(2, 1), sortingParameters);

        items.Select(item => item.Rank).Should().Equal(expected.Split(',').Select(int.Parse));
        page.Items.Should().Equal(items.Skip(1).Take(1));
        page.TotalCount.Should().Be(4);
        query.ToQueryString().Should().ContainAll("ORDER BY", "upper(", "LEFT JOIN");
    }

    [Fact(DisplayName = "Positional sorting preserves explicit initializers and supports applying sorting again")]
    public async Task Sorting_PreservesPositionalInitializers()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var query = repository.Products.Select(item => new PositionalProductView("unused", -1, null)
        {
            Label = item.Name,
            Rank = item.Score / 10
        });
        var sorted = PositionalProductSorting.ApplySorting(query, SortingParameters.None);

        var items = await PositionalProductSorting.ApplySorting(sorted, SortingParameters.Descending("rank"))
            .ToListAsync(TestContext.Current.CancellationToken);

        items.Select(item => item.Rank).Should().Equal(4, 3, 2, 1);
        items.Select(item => item.Label).Should().Equal("Gamma", "Alpha", "Beta", "Alpha");
    }

    [Fact(DisplayName = "Customized positional properties do not silently sort by raw constructor arguments")]
    public async Task Sorting_RejectsCustomizedConstructorProjectionInDatabase()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var query = repository.Products.Select(item => new CustomizedProductView(item.Score));
        var sorted = CustomizedProductSorting.ApplySorting(query, SortingParameters.Ascending("rank"));

        var translate = () => sorted.Take(2).ToQueryString();

        translate.Should().Throw<InvalidOperationException>().WithMessage("*could not be translated*");
    }

    [Fact(DisplayName = "Customized positional properties sort their transformed values in memory")]
    public void Sorting_UsesCustomizedPropertyValuesInMemory()
    {
        var query = new[] { 10, 30, 20 }.AsQueryable().Select(rank => new CustomizedProductView(rank));

        var items = CustomizedProductSorting.ApplySorting(query, SortingParameters.Ascending("rank")).Take(2).ToArray();

        items.Select(item => item.Rank).Should().Equal(-30, -20);
    }

    [Fact(DisplayName = "Explicit property initializers preserve customized values during database sorting and pagination")]
    public async Task Sorting_UsesExplicitCustomizedPropertyProjection()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var query = repository.Products.Select(item => new CustomizedProductView(0) { Rank = -item.Score });
        var sorted = CustomizedProductSorting.ApplySorting(query, SortingParameters.Ascending("rank")).Skip(1).Take(2);

        var items = await sorted.ToListAsync(TestContext.Current.CancellationToken);

        items.Select(item => item.Rank).Should().Equal(-30, -20);
        sorted.ToQueryString().Should().ContainAll("ORDER BY", "LIMIT", "OFFSET");
    }

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

    [Theory(DisplayName = "Generated sorting handles null projected rows and preserves key types")]
    [InlineData("label", SortDirection.Asc)]
    [InlineData("rank", SortDirection.Desc)]
    [InlineData("department.rank", SortDirection.Desc)]
    public void Sorting_HandlesNullProjectedRows(string field, SortDirection direction)
    {
        ProductView?[] items =
        [
            new() { Label = "B", Rank = -1, Department = new DepartmentView { Rank = -1 } },
            null,
            new() { Label = "A", Rank = -2, Department = new DepartmentView { Rank = -2 } }
        ];
        var sortingParameters = SortingParameters.Of(new SortField(field, direction));
        string?[] expected = direction == SortDirection.Asc ? [null, "A", "B"] : ["B", "A", null];
        var positionalQuery = items.AsQueryable().Select(item => item == null ? null
            : new PositionalProductView(item.Label, item.Rank,
                item.Department == null ? null : new PositionalDepartmentView(item.Department.Name, item.Department.Rank)));

        var regular = NullableProductViewSorting.ApplySorting(items.AsQueryable(), sortingParameters).ToArray();
        var positional = NullablePositionalProductSorting.ApplySorting(positionalQuery, sortingParameters).ToArray();

        regular.Select(item => item?.Label).Should().Equal(expected);
        positional.Select(item => item?.Label).Should().Equal(expected);
    }

    [Theory(DisplayName = "Generated sorting translates nullable root projections in PostgreSQL")]
    [InlineData("name", "A,A,Z,<null>")]
    [InlineData("rank", "Z,A,A,<null>")]
    public async Task Sorting_HandlesNullableRootProjectionInDatabase(string field, string expected)
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);
        var query = repository.Products.Select(item => item.Department == null ? null
            : new PositionalDepartmentView(item.Department.Name, item.Department.Rank));
        var sorted = NullableDepartmentViewSorting.ApplySorting(query, SortingParameters.Ascending(field));

        var items = await sorted.ToListAsync(TestContext.Current.CancellationToken);

        items.Select(item => item?.Name ?? "<null>").Should().Equal(expected.Split(','));
        sorted.ToQueryString().Should().ContainAll("LEFT JOIN", "ORDER BY");
    }

    [Theory(DisplayName = "Generated sorting handles nullable struct rows using the model's field paths")]
    [InlineData("name", SortDirection.Asc)]
    [InlineData("rank", SortDirection.Asc)]
    [InlineData("Rank", SortDirection.Desc)]
    public void Sorting_HandlesNullableStructRows(string field, SortDirection direction)
    {
        DepartmentStructView?[] items = [new("B", -1), null, new("A", -2)];
        var sortingParameters = SortingParameters.Of(new SortField(field, direction));
        string?[] expected = direction == SortDirection.Asc ? [null, "A", "B"] : ["B", "A", null];

        var sorted = NullableDepartmentStructSorting.ApplySorting(items.AsQueryable(), sortingParameters).ToArray();

        sorted.Select(item => item?.Name).Should().Equal(expected);
    }

    [Theory(DisplayName = "Generated sorting rejects nullable wrapper paths")]
    [InlineData("HasValue")]
    [InlineData("Value.Rank")]
    public void Sorting_RejectsNullableWrapperPaths(string field)
    {
        var query = Array.Empty<DepartmentStructView?>().AsQueryable();

        var action = () => NullableDepartmentStructSorting.ApplySorting(query, SortingParameters.Ascending(field));

        action.Should().Throw<ArgumentException>();
    }

    [Fact(DisplayName = "Projection grouping counts projected rows and sorts aggregate values")]
    public async Task Sorting_HandlesGrouping()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProjectionPageAsync<SummaryView, SummaryMapper, SummaryViewSorting>(
            new PaginationParameters(1, 1), SortingParameters.Descending(nameof(SummaryView.Count)));

        result.TotalCount.Should().Be(3);
        result.Items.Should().ContainSingle().Which.Should().Be(new SummaryView("Alpha", 2));
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

internal sealed record PositionalProductView(string Label, int Rank, PositionalDepartmentView? Department) : IReadModel;

internal sealed record PositionalDepartmentView(string Name, int Rank);

internal readonly record struct DepartmentStructView(string Name, int Rank);

internal sealed record CustomizedProductView(int Rank)
{
    public int Rank { get; init; } = -Rank;
}

internal sealed partial class CustomizedProductSorting : IReadModelSorting<CustomizedProductView>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
}

internal sealed class PositionalProductMapper : IReadModelMapper<int, ProductReadDbModel, PositionalProductView>
{
    public static IQueryable<PositionalProductView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new PositionalProductView(
            item.Name.ToUpper(), item.Score / 10,
            item.Department == null ? null : new PositionalDepartmentView(item.Department.Name, item.Department.Rank)));
    }
}

internal sealed partial class PositionalProductSorting : IReadModelSorting<PositionalProductView>
{
    public static SortingParameters DefaultSorting { get; } =
        SortingParameters.Of(new SortField(nameof(PositionalProductView.Label)), new SortField(nameof(PositionalProductView.Rank)));
}

internal sealed record SummaryView(string Label, int Count) : IReadModel;

internal sealed class SummaryMapper : IReadModelMapper<int, ProductReadDbModel, SummaryView>
{
    public static IQueryable<SummaryView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.GroupBy(item => item.Name).Select(group => new SummaryView(group.Key, group.Count()));
    }
}

internal sealed record LabelView(string Label) : IReadModel;

internal sealed class DistinctMapper : IReadModelMapper<int, ProductReadDbModel, LabelView>
{
    public static IQueryable<LabelView> ProjectTo(IQueryable<ProductReadDbModel> query)
    {
        return query.Select(item => new LabelView(item.Name)).Distinct();
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

internal sealed partial class NullableProductViewSorting : IReadModelSorting<ProductView?>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
}

internal sealed partial class NullablePositionalProductSorting : IReadModelSorting<PositionalProductView?>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
}

internal sealed partial class NullableDepartmentViewSorting : IReadModelSorting<PositionalDepartmentView?>
{
    public static SortingParameters DefaultSorting { get; } = SortingParameters.None;
}

internal sealed partial class NullableDepartmentStructSorting : IReadModelSorting<DepartmentStructView?>
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
