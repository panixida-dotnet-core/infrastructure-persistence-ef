using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

using PANiXiDA.Core.Application.Persistence.Errors;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Infrastructure;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests;

[Collection(PostgreSqlCollection.Name)]
public sealed class ConcurrencyExceptionTests(PostgreSqlContainerFixture fixture)
{
    [Theory(DisplayName = "Write context translates concurrent updates and deletions for every save overload")]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SaveChanges_TranslatesConcurrency(bool useAsync, bool explicitAcceptChanges, bool delete)
    {
        var database = PostgreSqlContainerFixture.CreateDatabaseName();
        var observer = new ConcurrencyObserver();
        await using var stale = await fixture.CreateInitializedDbContextAsync<ConcurrencyWriteDbContext>(
            database, options => new(options, [observer]));
        var entity = new TestAggregateRoot(TestAggregateRootId.New()) { Name = "Original" };
        stale.Add(entity);
        await stale.SaveChangesAsync(TestContext.Current.CancellationToken);
        await using var current = fixture.CreateDbContext<ConcurrencyWriteDbContext>(database, options => new(options, []));
        var changed = await current.Set<TestAggregateRoot>().SingleAsync(TestContext.Current.CancellationToken);
        if (delete)
        {
            current.Remove(changed);
        }
        else
        {
            changed.Name = "Concurrent update";
        }
        await current.SaveChangesAsync(TestContext.Current.CancellationToken);
        entity.Name = "Stale update";

        Func<Task> act = () => SaveAsync(stale, useAsync, explicitAcceptChanges);

        var failure = (await act.Should().ThrowAsync<ConcurrencyException>()).Which;
        failure.InnerException.Should().BeSameAs(observer.Exception);
        observer.Exception.Should().BeOfType<DbUpdateConcurrencyException>()
            .Which.Entries.Should().ContainSingle().Which.Entity.Should().BeSameAs(entity);
        stale.Entry(entity).State.Should().Be(EntityState.Modified);
    }

    [Fact(DisplayName = "Unit of work retains the translated conflict and rolls back the transaction")]
    public async Task UnitOfWork_PreservesConcurrencyException()
    {
        var observer = new ConcurrencyObserver();
        await using var context = await fixture.CreateInitializedDbContextAsync<ConcurrencyWriteDbContext>(
            options => new(options, [observer]));
        var entity = new TestAggregateRoot(TestAggregateRootId.New()) { Name = "Missing" };
        context.Update(entity);
        var unitOfWork = new EfUnitOfWork<ConcurrencyWriteDbContext>(context);

        Func<Task> act = () => unitOfWork.ExecuteInTransactionAsync(_ => Task.CompletedTask, TestContext.Current.CancellationToken);

        var failure = (await act.Should().ThrowAsync<ConcurrencyException>()).Which;
        failure.InnerException.Should().BeSameAs(observer.Exception);
        unitOfWork.HasActiveTransaction.Should().BeFalse();
        context.Database.CurrentTransaction.Should().BeNull();
    }

    [Theory(DisplayName = "Write context preserves unrelated failures and cancellation")]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task SaveChanges_PreservesOtherExceptions(bool useAsync, bool cancel)
    {
        Exception original = cancel ? new OperationCanceledException() : new DbUpdateException("Other database failure");
        await using var context = fixture.CreateDbContext<ConcurrencyWriteDbContext>(
            PostgreSqlContainerFixture.CreateDatabaseName(), options => new(options, [new SaveFailure(original)]));

        var actual = await Record.ExceptionAsync(() => SaveAsync(context, useAsync, false));

        actual.Should().BeSameAs(original);
    }

    [Theory(DisplayName = "Write context respects concurrency suppression by an interceptor")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SaveChanges_PreservesSuppression(bool useAsync)
    {
        var observer = new ConcurrencyObserver { Suppress = true };
        await using var context = await fixture.CreateInitializedDbContextAsync<ConcurrencyWriteDbContext>(
            options => new(options, [observer]));
        context.Update(new TestAggregateRoot(TestAggregateRootId.New()) { Name = "Missing" });

        var exception = await Record.ExceptionAsync(() => SaveAsync(context, useAsync, false));

        exception.Should().BeNull();
        observer.Exception.Should().BeOfType<DbUpdateConcurrencyException>();
    }

    private static Task<int> SaveAsync(DbContext context, bool useAsync, bool explicitAcceptChanges)
    {
        if (useAsync)
        {
            return explicitAcceptChanges
                ? context.SaveChangesAsync(false, TestContext.Current.CancellationToken)
                : context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        return Task.FromResult(explicitAcceptChanges ? context.SaveChanges(false) : context.SaveChanges());
    }

    internal sealed class ConcurrencyWriteDbContext(
        DbContextOptions<ConcurrencyWriteDbContext> options, IEnumerable<IInterceptor> interceptors)
        : WriteDbContext<ConcurrencyWriteDbContext>(options, interceptors)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<TestAggregateRoot>().Property(entity => entity.Name).IsConcurrencyToken();
        }
    }

    private sealed class ConcurrencyObserver : SaveChangesInterceptor
    {
        public Exception? Exception { get; private set; }
        public bool Suppress { get; init; }

        public override InterceptionResult ThrowingConcurrencyException(ConcurrencyExceptionEventData eventData, InterceptionResult result)
        {
            Exception = eventData.Exception;
            return Suppress ? InterceptionResult.Suppress() : result;
        }

        public override ValueTask<InterceptionResult> ThrowingConcurrencyExceptionAsync(
            ConcurrencyExceptionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult(ThrowingConcurrencyException(eventData, result));
        }
    }

    private sealed class SaveFailure(Exception exception) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) => throw exception;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) => throw exception;
    }
}
