using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        try
        {
            await action(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            currentTransaction = null;
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
        if (currentTransaction == null)
        {
            return;
        }

        try
        {
            await currentTransaction.CommitAsync(cancellationToken);
        }
        finally
        {
            await currentTransaction.DisposeAsync();
            currentTransaction = null;
        }
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken)
    {
        if (currentTransaction == null)
        {
            return;
        }

        try
        {
            await currentTransaction.RollbackAsync(cancellationToken);
        }
        finally
        {
            await currentTransaction.DisposeAsync();
            currentTransaction = null;
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeTransactionAsync()
    {
        if (currentTransaction == null)
        {
            return;
        }

        await currentTransaction.DisposeAsync();
        currentTransaction = null;
    }
}
