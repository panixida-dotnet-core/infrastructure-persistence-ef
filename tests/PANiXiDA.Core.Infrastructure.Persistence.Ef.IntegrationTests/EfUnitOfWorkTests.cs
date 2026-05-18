using Microsoft.EntityFrameworkCore;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfUnitOfWorkTests(PostgreSqlContainerFixture fixture)
{
    [Fact(DisplayName = "SaveChangesAsync persists pending changes")]
    public async Task SaveChangesAsync_PersistsPendingChanges()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        context.Entities.Add(new TransactionalEntity
        {
            Id = 1,
            Name = "Saved"
        });

        await unitOfWork.SaveChangesAsync(TestContext.Current.CancellationToken);

        var count = await CountEntitiesAsync(options);

        count.Should().Be(1);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync commits and saves changes")]
    public async Task ExecuteInTransactionAsync_CommitsAndSavesChanges()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);

        await unitOfWork.ExecuteInTransactionAsync(
            cancellationToken =>
            {
                context.Entities.Add(new TransactionalEntity
                {
                    Id = 1,
                    Name = "Committed"
                });

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        unitOfWork.HasActiveTransaction.Should().BeFalse();
        var count = await CountEntitiesAsync(options);

        count.Should().Be(1);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync rolls back and rethrows on failure")]
    public async Task ExecuteInTransactionAsync_RollsBackAndRethrows_OnFailure()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        var exception = new InvalidOperationException("Failure.");

        var act = () => unitOfWork.ExecuteInTransactionAsync(
            cancellationToken =>
            {
                context.Entities.Add(new TransactionalEntity
                {
                    Id = 1,
                    Name = "Rolled back"
                });

                throw exception;
            },
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Failure.");
        unitOfWork.HasActiveTransaction.Should().BeFalse();
        var count = await CountEntitiesAsync(options);

        count.Should().Be(0);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync uses active transaction without saving changes")]
    public async Task ExecuteInTransactionAsync_UsesActiveTransactionWithoutSavingChanges()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await unitOfWork.ExecuteInTransactionAsync(
            cancellationToken =>
            {
                context.Entities.Add(new TransactionalEntity
                {
                    Id = 1,
                    Name = "Not saved"
                });

                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken);

        unitOfWork.HasActiveTransaction.Should().BeTrue();
        var count = await CountEntitiesAsync(options);

        count.Should().Be(0);

        await unitOfWork.RollbackTransactionAsync(TestContext.Current.CancellationToken);
    }

    [Fact(DisplayName = "BeginTransactionAsync is idempotent and CommitTransactionAsync commits active transaction")]
    public async Task BeginTransactionAsyncIsIdempotentAndCommitTransactionAsync_CommitsActiveTransaction()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);

        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        context.Entities.Add(new TransactionalEntity
        {
            Id = 1,
            Name = "Committed"
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        await unitOfWork.CommitTransactionAsync(TestContext.Current.CancellationToken);

        unitOfWork.HasActiveTransaction.Should().BeFalse();
        var count = await CountEntitiesAsync(options);

        count.Should().Be(1);
    }

    [Fact(DisplayName = "RollbackTransactionAsync rolls back active transaction")]
    public async Task RollbackTransactionAsync_RollsBackActiveTransaction()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);
        context.Entities.Add(new TransactionalEntity
        {
            Id = 1,
            Name = "Rolled back"
        });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);

        await unitOfWork.RollbackTransactionAsync(TestContext.Current.CancellationToken);

        unitOfWork.HasActiveTransaction.Should().BeFalse();
        var count = await CountEntitiesAsync(options);

        count.Should().Be(0);
    }

    [Fact(DisplayName = "Transaction methods return when no active transaction exists")]
    public async Task TransactionMethods_Return_WhenNoActiveTransactionExists()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);

        await unitOfWork.CommitTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.RollbackTransactionAsync(TestContext.Current.CancellationToken);
        await unitOfWork.DisposeTransactionAsync();

        unitOfWork.HasActiveTransaction.Should().BeFalse();
    }

    [Fact(DisplayName = "DisposeTransactionAsync disposes active transaction")]
    public async Task DisposeTransactionAsync_DisposesActiveTransaction()
    {
        var options = await CreateInitializedOptionsAsync();

        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        await unitOfWork.DisposeTransactionAsync();

        unitOfWork.HasActiveTransaction.Should().BeFalse();
    }

    private async Task<DbContextOptions<TransactionalDbContext>> CreateInitializedOptionsAsync()
    {
        var options = fixture.CreateOptions<TransactionalDbContext>(
            PostgreSqlContainerFixture.CreateDatabaseName());
        await EnsureCreatedAsync(options);
        return options;
    }

    private static async Task EnsureCreatedAsync(DbContextOptions<TransactionalDbContext> options)
    {
        await using var context = new TransactionalDbContext(options);
        await context.Database.EnsureCreatedAsync(TestContext.Current.CancellationToken);
    }

    private static async Task<int> CountEntitiesAsync(DbContextOptions<TransactionalDbContext> options)
    {
        await using var context = new TransactionalDbContext(options);
        return await context.Entities.CountAsync(TestContext.Current.CancellationToken);
    }
}
