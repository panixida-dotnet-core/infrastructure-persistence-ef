using System.Reflection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class EfUnitOfWorkTests(PostgreSqlContainerFixture fixture)
{
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
            async cancellationToken =>
            {
                context.Entities.Add(new TransactionalEntity
                {
                    Id = 1,
                    Name = "Rolled back"
                });

                await Task.Yield();

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

    [Fact(DisplayName = "CommitTransactionAsync disposes and clears a failed transaction")]
    public async Task CommitTransactionAsync_DisposesAndClearsFailedTransaction()
    {
        await using var context = new TransactionalDbContext(
            new DbContextOptionsBuilder<TransactionalDbContext>().Options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        var exception = new InvalidOperationException("Commit failed.");
        var transaction = new FailingDbContextTransaction(commitException: exception);
        SetCurrentTransaction(unitOfWork, transaction);

        var act = () => unitOfWork.CommitTransactionAsync(
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit failed.");
        transaction.IsDisposed.Should().BeTrue();
        GetCurrentTransaction(unitOfWork).Should().BeNull();
    }

    [Fact(DisplayName = "RollbackTransactionAsync disposes and clears a failed transaction")]
    public async Task RollbackTransactionAsync_DisposesAndClearsFailedTransaction()
    {
        await using var context = new TransactionalDbContext(
            new DbContextOptionsBuilder<TransactionalDbContext>().Options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        var exception = new InvalidOperationException("Rollback failed.");
        var transaction = new FailingDbContextTransaction(rollbackException: exception);
        SetCurrentTransaction(unitOfWork, transaction);

        var act = () => unitOfWork.RollbackTransactionAsync(
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Rollback failed.");
        transaction.IsDisposed.Should().BeTrue();
        GetCurrentTransaction(unitOfWork).Should().BeNull();
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

    private static void SetCurrentTransaction(
        EfUnitOfWork<TransactionalDbContext> unitOfWork,
        IDbContextTransaction transaction)
    {
        var field = typeof(EfUnitOfWork<TransactionalDbContext>).GetField(
            "currentTransaction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new MissingFieldException(
                typeof(EfUnitOfWork<TransactionalDbContext>).FullName,
                "currentTransaction");
        }

        field.SetValue(unitOfWork, transaction);
    }

    private static IDbContextTransaction? GetCurrentTransaction(
        EfUnitOfWork<TransactionalDbContext> unitOfWork)
    {
        var field = typeof(EfUnitOfWork<TransactionalDbContext>).GetField(
            "currentTransaction",
            BindingFlags.Instance | BindingFlags.NonPublic);
        if (field is null)
        {
            throw new MissingFieldException(
                typeof(EfUnitOfWork<TransactionalDbContext>).FullName,
                "currentTransaction");
        }

        return field.GetValue(unitOfWork) as IDbContextTransaction;
    }

    private sealed class FailingDbContextTransaction(
        Exception? commitException = null,
        Exception? rollbackException = null) : IDbContextTransaction
    {
        public Guid TransactionId { get; } = Guid.NewGuid();

        public bool SupportsSavepoints => false;

        public bool IsDisposed { get; private set; }

        public void Commit()
        {
            if (commitException is not null)
            {
                throw commitException;
            }
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            return commitException is null
                ? Task.CompletedTask
                : Task.FromException(commitException);
        }

        public void Rollback()
        {
            if (rollbackException is not null)
            {
                throw rollbackException;
            }
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            return rollbackException is null
                ? Task.CompletedTask
                : Task.FromException(rollbackException);
        }

        public void Dispose()
        {
            IsDisposed = true;
        }

        public ValueTask DisposeAsync()
        {
            IsDisposed = true;
            return ValueTask.CompletedTask;
        }

        public void CreateSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task CreateSavepointAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new NotSupportedException());
        }

        public void RollbackToSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task RollbackToSavepointAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new NotSupportedException());
        }

        public void ReleaseSavepoint(string name)
        {
            throw new NotSupportedException();
        }

        public Task ReleaseSavepointAsync(
            string name,
            CancellationToken cancellationToken = default)
        {
            return Task.FromException(new NotSupportedException());
        }
    }
}
