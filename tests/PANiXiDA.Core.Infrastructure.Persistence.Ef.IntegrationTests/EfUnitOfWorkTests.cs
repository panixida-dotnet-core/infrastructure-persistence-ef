using System.Data.Common;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

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
        var exception = new InvalidOperationException("Commit failed.");
        var options = await CreateInitializedOptionsAsync(
            new FailingCommitTransactionInterceptor(exception));
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var act = () => unitOfWork.CommitTransactionAsync(
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Commit failed.");
        unitOfWork.HasActiveTransaction.Should().BeFalse();

        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        unitOfWork.HasActiveTransaction.Should().BeTrue();
        await unitOfWork.DisposeTransactionAsync();
    }

    [Fact(DisplayName = "RollbackTransactionAsync disposes and clears a failed transaction")]
    public async Task RollbackTransactionAsync_DisposesAndClearsFailedTransaction()
    {
        var exception = new InvalidOperationException("Rollback failed.");
        var options = await CreateInitializedOptionsAsync(
            new FailingRollbackTransactionInterceptor(exception));
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        var act = () => unitOfWork.RollbackTransactionAsync(
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Rollback failed.");
        unitOfWork.HasActiveTransaction.Should().BeFalse();

        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        unitOfWork.HasActiveTransaction.Should().BeTrue();
        await unitOfWork.DisposeTransactionAsync();
    }

    [Theory(DisplayName = "ExecuteInTransactionAsync preserves all operation, rollback and disposal failures")]
    [InlineData("action", false, false)]
    [InlineData("action", true, false)]
    [InlineData("action", false, true)]
    [InlineData("action", true, true)]
    [InlineData("save", false, false)]
    [InlineData("save", true, false)]
    [InlineData("save", false, true)]
    [InlineData("save", true, true)]
    [InlineData("commit", false, false)]
    [InlineData("commit", true, false)]
    [InlineData("commit", false, true)]
    [InlineData("commit", true, true)]
    public async Task ExecuteInTransactionAsync_PreservesAllFailures(
        string failureStage,
        bool failRollback,
        bool failDispose)
    {
        // Arrange
        var originalException = new InvalidOperationException("Operation failed.");
        var rollbackException = failRollback ? new InvalidOperationException("Rollback failed.") : null;
        var disposeException = failDispose ? new InvalidOperationException("Dispose failed.") : null;
        var interceptor = new TransactionFailureInterceptor(
            failureStage == "commit" ? originalException : null, rollbackException);
        var options = await CreateInitializedOptionsAsync(interceptor);
        var disposeCalls = 0;
        options = new DbContextOptionsBuilder<TransactionalDbContext>(options)
            .AddInterceptors(new SaveFailureInterceptor(failureStage == "save" ? originalException : null))
            .LogTo(_ =>
            {
                disposeCalls++;
                if (disposeException is not null)
                {
                    throw disposeException;
                }
            }, [RelationalEventId.TransactionDisposed])
            .Options;
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        var expectedExceptions = new[] { originalException, rollbackException, disposeException }
            .OfType<Exception>().ToArray();

        // Act
        var actualException = await Record.ExceptionAsync(() => unitOfWork.ExecuteInTransactionAsync(
            _ => failureStage == "action" ? ThrowOperationAsync(originalException) : Task.CompletedTask,
            TestContext.Current.CancellationToken));

        // Assert
        AssertFailures(actualException, expectedExceptions);
        interceptor.RollbackCalls.Should().Be(1);
        disposeCalls.Should().Be(1);
        originalException.StackTrace.Should().Contain(failureStage switch
        {
            "action" => nameof(ThrowOperationAsync),
            "save" => nameof(SaveFailureInterceptor.SavingChangesAsync),
            _ => nameof(TransactionFailureInterceptor.TransactionCommittingAsync)
        });
        if (rollbackException is not null)
        {
            rollbackException.StackTrace.Should().Contain(nameof(TransactionFailureInterceptor.TransactionRollingBackAsync));
        }

        if (disposeException is not null)
        {
            disposeException.StackTrace.Should().Contain(nameof(ExecuteInTransactionAsync_PreservesAllFailures));
        }

        await unitOfWork.DisposeTransactionAsync();
        disposeCalls.Should().Be(1);
    }

    [Theory(DisplayName = "Transaction completion preserves single failures and aggregates disposal failures")]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    public async Task CompleteTransactionAsync_PreservesAllFailures(
        bool commit,
        bool failCompletion,
        bool failDispose)
    {
        // Arrange
        var completionException = failCompletion ? new InvalidOperationException("Completion failed.") : null;
        var disposeException = failDispose ? new InvalidOperationException("Dispose failed.") : null;
        var interceptor = new TransactionFailureInterceptor(
            commit ? completionException : null, commit ? null : completionException);
        var options = await CreateInitializedOptionsAsync(interceptor);
        var disposeCalls = 0;
        options = new DbContextOptionsBuilder<TransactionalDbContext>(options)
            .LogTo(_ =>
            {
                disposeCalls++;
                if (disposeException is not null)
                {
                    throw disposeException;
                }
            }, [RelationalEventId.TransactionDisposed])
            .Options;
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        // Act
        var actualException = await Record.ExceptionAsync(() => commit
            ? unitOfWork.CommitTransactionAsync(TestContext.Current.CancellationToken)
            : unitOfWork.RollbackTransactionAsync(TestContext.Current.CancellationToken));

        // Assert
        AssertFailures(actualException, new[] { completionException, disposeException }.OfType<Exception>().ToArray());
        disposeCalls.Should().Be(1);
        await unitOfWork.DisposeTransactionAsync();
        disposeCalls.Should().Be(1);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync preserves disposal failure after a successful commit")]
    public async Task ExecuteInTransactionAsync_WhenOnlyDisposalFails_PreservesFailure()
    {
        // Arrange
        var exception = new InvalidOperationException("Dispose failed.");
        var interceptor = new TransactionFailureInterceptor(null, null);
        var options = await CreateInitializedOptionsAsync(interceptor);
        options = new DbContextOptionsBuilder<TransactionalDbContext>(options)
            .LogTo(_ => throw exception, [RelationalEventId.TransactionDisposed])
            .Options;
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);

        // Act
        var actualException = await Record.ExceptionAsync(() => unitOfWork.ExecuteInTransactionAsync(
            _ => Task.CompletedTask, TestContext.Current.CancellationToken));

        // Assert
        actualException.Should().BeSameAs(exception);
        interceptor.RollbackCalls.Should().Be(0);
        await unitOfWork.DisposeTransactionAsync();
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync preserves a single cancellation and its original stack")]
    public async Task ExecuteInTransactionAsync_WhenOnlyOperationIsCanceled_PreservesCancellation()
    {
        // Arrange
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var exception = new OperationCanceledException(cancellation.Token);
        var options = await CreateInitializedOptionsAsync();
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);

        // Act
        var task = unitOfWork.ExecuteInTransactionAsync(
            _ => ThrowOperationAsync(exception), TestContext.Current.CancellationToken);
        var actualException = await Record.ExceptionAsync(() => task);

        // Assert
        actualException.Should().BeSameAs(exception);
        exception.StackTrace.Should().Contain(nameof(ThrowOperationAsync));
        task.IsCanceled.Should().BeTrue();
        unitOfWork.HasActiveTransaction.Should().BeFalse();
    }

    [Fact(DisplayName = "DisposeTransactionAsync preserves a single disposal failure and releases ownership")]
    public async Task DisposeTransactionAsync_WhenDisposalFails_PreservesFailure()
    {
        // Arrange
        var exception = new InvalidOperationException("Dispose failed.");
        var options = await CreateInitializedOptionsAsync();
        var disposeCalls = 0;
        options = new DbContextOptionsBuilder<TransactionalDbContext>(options)
            .LogTo(_ =>
            {
                disposeCalls++;
                throw exception;
            }, [RelationalEventId.TransactionDisposed])
            .Options;
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);
        await unitOfWork.BeginTransactionAsync(TestContext.Current.CancellationToken);

        // Act
        var actualException = await Record.ExceptionAsync(async () => await unitOfWork.DisposeTransactionAsync());

        // Assert
        actualException.Should().BeSameAs(exception);
        exception.StackTrace.Should().Contain(nameof(DisposeTransactionAsync_WhenDisposalFails_PreservesFailure));
        await unitOfWork.DisposeTransactionAsync();
        disposeCalls.Should().Be(1);
    }

    [Fact(DisplayName = "ExecuteInTransactionAsync preserves an existing aggregate when cleanup succeeds")]
    public async Task ExecuteInTransactionAsync_WhenOnlyActionThrowsAggregate_PreservesInstance()
    {
        // Arrange
        var exception = new AggregateException(new InvalidOperationException("First failure."), new ArgumentException("Second failure."));
        var options = await CreateInitializedOptionsAsync();
        await using var context = new TransactionalDbContext(options);
        var unitOfWork = new EfUnitOfWork<TransactionalDbContext>(context);

        // Act
        var actualException = await Record.ExceptionAsync(() => unitOfWork.ExecuteInTransactionAsync(
            _ => ThrowOperationAsync(exception), TestContext.Current.CancellationToken));

        // Assert
        actualException.Should().BeSameAs(exception);
        exception.StackTrace.Should().Contain(nameof(ThrowOperationAsync));
        unitOfWork.HasActiveTransaction.Should().BeFalse();
    }

    private static async Task ThrowOperationAsync(Exception exception)
    {
        await Task.Yield();
        throw exception;
    }

    private static void AssertFailures(Exception? actualException, Exception[] expectedExceptions)
    {
        if (expectedExceptions.Length == 1)
        {
            actualException.Should().BeSameAs(expectedExceptions[0]);
        }
        else
        {
            var aggregate = actualException.Should().BeOfType<AggregateException>().Subject;
            aggregate.InnerExceptions.Should().Equal(expectedExceptions);
        }
    }

    private async Task<DbContextOptions<TransactionalDbContext>> CreateInitializedOptionsAsync(
        IInterceptor? interceptor = null)
    {
        var options = fixture.CreateOptions<TransactionalDbContext>(
            PostgreSqlContainerFixture.CreateDatabaseName());
        await EnsureCreatedAsync(options);

        if (interceptor is null)
        {
            return options;
        }

        return new DbContextOptionsBuilder<TransactionalDbContext>(options)
            .AddInterceptors(interceptor)
            .Options;
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

    private sealed class SaveFailureInterceptor(Exception? exception) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (exception is not null)
            {
                throw exception;
            }

            return new ValueTask<InterceptionResult<int>>(result);
        }
    }

    private sealed class TransactionFailureInterceptor(Exception? commitException, Exception? rollbackException)
        : DbTransactionInterceptor
    {
        public int RollbackCalls { get; private set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            if (commitException is not null)
            {
                throw commitException;
            }

            return new ValueTask<InterceptionResult>(result);
        }

        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            RollbackCalls++;
            if (rollbackException is not null)
            {
                throw rollbackException;
            }

            return new ValueTask<InterceptionResult>(result);
        }
    }

    private sealed class FailingCommitTransactionInterceptor(Exception exception)
        : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<InterceptionResult>(exception);
        }
    }

    private sealed class FailingRollbackTransactionInterceptor(Exception exception)
        : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionRollingBackAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default)
        {
            return ValueTask.FromException<InterceptionResult>(exception);
        }
    }
}
