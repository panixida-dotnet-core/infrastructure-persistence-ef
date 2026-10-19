using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using System.Runtime.ExceptionServices;

using PANiXiDA.Core.Application.Persistence;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

/// <summary>
/// Coordinates EF Core transaction boundaries for a write DbContext.
/// </summary>
/// <remarks>
/// A single failure is rethrown with its original instance and stack trace. Multiple failures from
/// transaction execution, completion, rollback or disposal are reported in an <see cref="AggregateException" />.
/// </remarks>
/// <typeparam name="TDbContext">The EF Core DbContext type used by the unit of work.</typeparam>
/// <param name="dbContext">The DbContext used to manage transactions.</param>
public sealed class EfUnitOfWork<TDbContext>(TDbContext dbContext) : IUnitOfWork
    where TDbContext : DbContext
{
    private IDbContextTransaction? currentTransaction;

    /// <inheritdoc />
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

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

        var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        currentTransaction = transaction;
        List<Exception>? exceptions = null;

        try
        {
            await action(cancellationToken);
            await SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (Exception caughtException)
        {
            exceptions = [caughtException];
        }

        TakeCurrentTransaction();

        if (exceptions is not null)
        {
            try
            {
                await transaction.RollbackAsync(cancellationToken);
            }
            catch (Exception rollbackException)
            {
                exceptions.Add(rollbackException);
            }
        }

        await DisposeTransactionAsync(transaction, exceptions);
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
    public Task CommitTransactionAsync(CancellationToken cancellationToken)
    {
        return CompleteTransactionAsync(
            static (transaction, token) => transaction.CommitAsync(token),
            cancellationToken);
    }

    /// <inheritdoc />
    public Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        return CompleteTransactionAsync(
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

        List<Exception>? exceptions = null;
        try
        {
            await completeTransactionAsync(transaction, cancellationToken);
        }
        catch (Exception exception)
        {
            exceptions = [exception];
        }

        await DisposeTransactionAsync(transaction, exceptions);
    }

    private static async Task DisposeTransactionAsync(IDbContextTransaction transaction, List<Exception>? exceptions)
    {
        try
        {
            await transaction.DisposeAsync();
        }
        catch (Exception disposeException)
        {
            (exceptions ??= []).Add(disposeException);
        }

        if (exceptions is null)
        {
            return;
        }

        if (exceptions.Count == 1)
        {
            ExceptionDispatchInfo.Throw(exceptions[0]);
        }

        throw new AggregateException("Multiple errors occurred while completing the transaction.", exceptions);
    }

    /// <inheritdoc />
    public ValueTask DisposeTransactionAsync()
    {
        var transaction = TakeCurrentTransaction();
        return transaction?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    private IDbContextTransaction? TakeCurrentTransaction()
    {
        var transaction = currentTransaction;
        currentTransaction = null;
        return transaction;
    }
}
