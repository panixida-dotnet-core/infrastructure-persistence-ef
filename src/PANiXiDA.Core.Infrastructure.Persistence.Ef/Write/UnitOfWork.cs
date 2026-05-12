using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using PANiXiDA.Core.Application.Persistence;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

public sealed class UnitOfWork<TDbContext>(TDbContext dbContext) : IUnitOfWork
    where TDbContext : DbContext
{
    private IDbContextTransaction? currentTransaction;

    public bool HasActiveTransaction
    {
        get
        {
            return dbContext.Database.CurrentTransaction != null;
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return dbContext.SaveChangesAsync(cancellationToken);
    }

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

    public async Task BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (currentTransaction != null)
        {
            return;
        }

        currentTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
    }

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
