using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Application.Querying.Cursor;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfReadRepositoryTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "AnyAsync and ExistsByIdAsync return repository state")]
    public async Task AnyAsyncAndExistsByIdAsync_ReturnRepositoryState()
    {
        await using var context = await CreateContextAsync();
        context.Set<ProductReadDbModel>().Add(new ProductReadDbModel
        {
            Id = 1,
            Name = "One",
            Score = 10
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new ExposedReadRepository(context);

        var any = await repository.AnyAsync(TestContext.Current.CancellationToken);
        var existing = await repository.ExistsByIdAsync(1, TestContext.Current.CancellationToken);
        var missing = await repository.ExistsByIdAsync(2, TestContext.Current.CancellationToken);

        any.Should().BeTrue();
        existing.Should().BeTrue();
        missing.Should().BeFalse();
    }

    [Fact(DisplayName = "AnyAsync returns false for empty repository")]
    public async Task AnyAsync_ReturnsFalse_ForEmptyRepository()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var any = await repository.AnyAsync(TestContext.Current.CancellationToken);

        any.Should().BeFalse();
    }

    [Fact(DisplayName = "GetByIdAsync projects read model when found")]
    public async Task GetByIdAsync_ProjectsReadModel_WhenFound()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var readModel = await repository.GetProductByIdAsync(2);

        readModel.Should().Be(new ProductReadModel(2, "Beta", 20));
    }

    [Fact(DisplayName = "GetByIdAsync returns null when read model is missing")]
    public async Task GetByIdAsync_ReturnsNull_WhenReadModelIsMissing()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var readModel = await repository.GetProductByIdAsync(404);

        readModel.Should().BeNull();
    }

    [Fact(DisplayName = "GetPagedResultAsync returns empty page when source query is empty")]
    public async Task GetPagedResultAsync_ReturnsEmptyPage_WhenSourceQueryIsEmpty()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProductsPageAsync(
            repository.Products,
            new PaginationParameters(3, 5),
            SortParameters.Default());

        result.Items.Should().BeEmpty();
        result.PageNumber.Should().Be(3);
        result.PageSize.Should().Be(5);
        result.TotalCount.Should().Be(0);
        result.TotalPages.Should().Be(0);
        result.HasPreviousPage.Should().BeTrue();
        result.HasNextPage.Should().BeFalse();
    }

    [Fact(DisplayName = "GetPagedResultAsync sorts, paginates, and projects items")]
    public async Task GetPagedResultAsync_SortsPaginatesAndProjectsItems()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var result = await repository.GetProductsPageAsync(
            repository.Products,
            new PaginationParameters(1, 2),
            new SortParameters(nameof(ProductReadDbModel.Score), SortOrder.Descending));

        result.Items.Should().Equal(
            new ProductReadModel(3, "Gamma", 30),
            new ProductReadModel(2, "Beta", 20));
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(2);
        result.TotalCount.Should().Be(3);
        result.TotalPages.Should().Be(2);
        result.HasPreviousPage.Should().BeFalse();
        result.HasNextPage.Should().BeTrue();
    }

    [Fact(DisplayName = "ApplyPagination skips and takes items")]
    public async Task ApplyPagination_SkipsAndTakesItems()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var ids = await repository.ApplyPaginationForTest(
                repository.Products.OrderBy(item => item.Id),
                new PaginationParameters(2, 1))
            .Select(item => item.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal(2);
    }

    [Fact(DisplayName = "ApplySort uses Id descending when field is blank")]
    public async Task ApplySort_UsesIdDescending_WhenFieldIsBlank()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var ids = await repository.ApplySortForTest(
                repository.Products,
                new SortParameters(" ", SortOrder.Ascending))
            .Select(item => item.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal(3, 2, 1);
    }

    [Fact(DisplayName = "ApplySort sorts by Id when Id field is requested")]
    public async Task ApplySort_SortsById_WhenIdFieldIsRequested()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var ids = await repository.ApplySortForTest(
                repository.Products,
                new SortParameters("id", SortOrder.Ascending))
            .Select(item => item.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal(1, 2, 3);
    }

    [Fact(DisplayName = "ApplySort sorts by custom field and then by Id descending")]
    public async Task ApplySort_SortsByCustomFieldAndThenByIdDescending()
    {
        await using var context = await CreateContextAsync();
        context.Set<ProductReadDbModel>().AddRange(
            new ProductReadDbModel
            {
                Id = 1,
                Name = "Same",
                Score = 10
            },
            new ProductReadDbModel
            {
                Id = 2,
                Name = "Same",
                Score = 20
            });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        var repository = new ExposedReadRepository(context);

        var ids = await repository.ApplySortForTest(
                repository.Products,
                new SortParameters(nameof(ProductReadDbModel.Name), SortOrder.Ascending))
            .Select(item => item.Id)
            .ToListAsync(TestContext.Current.CancellationToken);

        ids.Should().Equal(2, 1);
    }

    [Fact(DisplayName = "GetCursorResultAsync returns empty result with minimum limit")]
    public async Task GetCursorResultAsync_ReturnsEmptyResultWithMinimumLimit()
    {
        await using var context = await CreateContextAsync();
        var repository = new ExposedReadRepository(context);

        var result = await ExposedReadRepository.GetCursorPageAsync(
            repository.Products,
            new CursorPaginationParameters(null, 0, CursorDirection.Forward),
            item => item.Id.ToString());

        result.Items.Should().BeEmpty();
        result.Limit.Should().Be(1);
        result.NextCursor.Should().BeNull();
        result.PreviousCursor.Should().BeNull();
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact(DisplayName = "GetCursorResultAsync returns forward page with next cursor")]
    public async Task GetCursorResultAsync_ReturnsForwardPageWithNextCursor()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var result = await ExposedReadRepository.GetCursorPageAsync(
            repository.Products.OrderBy(item => item.Id),
            CursorPaginationParameters.FirstPage(2),
            item => item.Id.ToString());

        result.Items.Select(item => item.Id).Should().Equal(1, 2);
        result.Limit.Should().Be(2);
        result.NextCursor.Should().Be("2");
        result.PreviousCursor.Should().BeNull();
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeFalse();
    }

    [Fact(DisplayName = "GetCursorResultAsync returns forward page with previous cursor")]
    public async Task GetCursorResultAsync_ReturnsForwardPageWithPreviousCursor()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var result = await ExposedReadRepository.GetCursorPageAsync(
            repository.Products.Where(item => item.Id > 2).OrderBy(item => item.Id),
            new CursorPaginationParameters("2", 2, CursorDirection.Forward),
            item => item.Id.ToString());

        result.Items.Select(item => item.Id).Should().Equal(3);
        result.NextCursor.Should().BeNull();
        result.PreviousCursor.Should().Be("3");
        result.HasNextPage.Should().BeFalse();
        result.HasPreviousPage.Should().BeTrue();
    }

    [Fact(DisplayName = "GetCursorResultAsync returns backward page in natural order")]
    public async Task GetCursorResultAsync_ReturnsBackwardPageInNaturalOrder()
    {
        await using var context = await CreateContextAsync();
        await SeedProductsAsync(context);
        var repository = new ExposedReadRepository(context);

        var result = await ExposedReadRepository.GetCursorPageAsync(
            repository.Products.Where(item => item.Id < 4).OrderByDescending(item => item.Id),
            new CursorPaginationParameters("4", 2, CursorDirection.Backward),
            item => item.Id.ToString());

        result.Items.Select(item => item.Id).Should().Equal(2, 3);
        result.NextCursor.Should().Be("3");
        result.PreviousCursor.Should().Be("2");
        result.HasNextPage.Should().BeTrue();
        result.HasPreviousPage.Should().BeTrue();
    }

    private Task<WritableReadDbContext> CreateContextAsync()
    {
        return fixture.CreateInitializedDbContextAsync<WritableReadDbContext>(
            options => new WritableReadDbContext(options));
    }

    private static async Task SeedProductsAsync(WritableReadDbContext context)
    {
        context.Set<ProductReadDbModel>().AddRange(
            new ProductReadDbModel
            {
                Id = 1,
                Name = "Alpha",
                Score = 10
            },
            new ProductReadDbModel
            {
                Id = 2,
                Name = "Beta",
                Score = 20
            },
            new ProductReadDbModel
            {
                Id = 3,
                Name = "Gamma",
                Score = 30
            });

        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
