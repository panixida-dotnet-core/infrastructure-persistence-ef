using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using System.Runtime.ExceptionServices;

using PANiXiDA.Core.Application.Persistence;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Coordinates EF Core transaction boundaries for a write DbContext.
/// </summary>
/// <typeparam name="TDbContext">The EF Core DbContext type used by the unit of work.</typeparam>
/// <param name="dbContext">The DbContext used to manage transactions.</param>
public sealed class EfUnitOfWork<TDbContext>(TDbContext dbContext) : IUnitOfWork
    where TDbContext : DbContext
{
    private IDbContextTransaction? currentTransaction;

    /// <inheritdoc />
    public bool HasActiveTransaction
    {
        get
        {
            return dbContext.Database.CurrentTransaction != null;
        }
    }

    /// <inheritdoc />
    public async Task ExecuteInTransactionAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (currentTransaction != null)
        {
            await action(cancellationToken);
            return;
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        currentTransaction = transaction;
        Exception? exception = null;

        try
        {
            await action(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception caughtException)
        {
            exception = caughtException;
        }

        TakeCurrentTransaction();

        if (exception is not null)
        {
            await transaction.RollbackAsync(cancellationToken);
            ExceptionDispatchInfo.Throw(exception);
        }
    }

    /// <inheritdoc />
    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (currentTransaction != null)
        {
            return;
        }

        currentTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        await CompleteTransactionAsync(
            static (transaction, token) => transaction.CommitAsync(token),
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        await CompleteTransactionAsync(
            static (transaction, token) => transaction.RollbackAsync(token),
            cancellationToken);
    }

    private async Task CompleteTransactionAsync(
        Func<IDbContextTransaction, CancellationToken, Task> completeTransactionAsync,
        CancellationToken cancellationToken)
    {
        var transaction = TakeCurrentTransaction();
        if (transaction == null)
        {
            return;
        }

        await using (transaction)
        {
            await completeTransactionAsync(transaction, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeTransactionAsync()
    {
        var transaction = TakeCurrentTransaction();
        if (transaction == null)
        {
            return;
        }

        await transaction.DisposeAsync();
    }

    private IDbContextTransaction? TakeCurrentTransaction()
    {
        var transaction = currentTransaction;
        currentTransaction = null;
        return transaction;
    }
}
