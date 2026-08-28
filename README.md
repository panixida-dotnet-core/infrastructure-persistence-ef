# PANiXiDA.Core.Infrastructure.Persistence.Ef

`PANiXiDA.Core.Infrastructure.Persistence.Ef` is a .NET library that provides Entity Framework Core persistence infrastructure for PANiXiDA Core applications.

It is designed for application and infrastructure packages that use `PANiXiDA.Core.Application` persistence abstractions, PostgreSQL, DDD aggregate roots, and read models.

## Status

[![CI](https://github.com/panixida-dotnet-core/infrastructure-persistence-ef/actions/workflows/ci.yml/badge.svg)](https://github.com/panixida-dotnet-core/infrastructure-persistence-ef/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/PANiXiDA.Core.Infrastructure.Persistence.Ef.svg)](https://www.nuget.org/packages/PANiXiDA.Core.Infrastructure.Persistence.Ef)
[![NuGet downloads](https://img.shields.io/nuget/dt/PANiXiDA.Core.Infrastructure.Persistence.Ef.svg)](https://www.nuget.org/packages/PANiXiDA.Core.Infrastructure.Persistence.Ef)
[![Target Framework](https://img.shields.io/badge/target-net10.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/github/license/panixida-dotnet-core/infrastructure-persistence-ef.svg)](LICENSE)

## Overview

The package bridges PANiXiDA application-layer persistence contracts with EF Core. It provides base write and read DbContexts, repository base classes, a unit of work implementation, audit shadow properties, soft-delete behavior, PostgreSQL DI registration, and helpers for read-model sorting and pagination.

The library is intentionally infrastructure-focused. Domain model design, command/query handlers, and concrete repositories stay in consuming applications.

## Features

- PostgreSQL registration extensions for write/read EF Core infrastructure and scoped repository implementation auto-registration.
- `WriteDbContext<TDbContext>` with HiLo configuration, optional context-derived schema naming, assembly configuration scanning, and plural table names.
- `ReadDbContext<TDbContext>` with no-tracking queries, automatic read model registration, optional context-derived schema naming, and migration exclusion for read models.
- Base `EfRepository<TDbContext, TId, TAggregateRoot>` with async persistence operations integrated with `IAggregateTracker`.
- `AggregateTracker` implementation for tracking touched aggregate roots independently of EF Core.
- `EfUnitOfWork<TDbContext>` implementation for transaction boundaries.
- Keyed `IUnitOfWork` registration by write `DbContext` type for modular applications.
- Auditable entity configuration with `CreatedAt`, `UpdatedAt`, and `DeletedAt` shadow properties.
- SaveChanges interceptor that updates audit values and converts deletes with `DeletedAt` into soft deletes.
- Read repository helpers for page-based pagination, cursor pagination, dynamic sorting, and projection through `IReadModelMapper`.

## Quick Start

### Requirements

- .NET 10 SDK
- PostgreSQL when using the built-in DI registration methods
- Docker for local integration tests because they use Testcontainers with PostgreSQL

### Installation

```bash
dotnet add package PANiXiDA.Core.Infrastructure.Persistence.Ef
```

### Configuration

The built-in PostgreSQL registration methods read the connection string named `PostgreSqlConnectionString`.

```json
{
  "ConnectionStrings": {
    "PostgreSqlConnectionString": "Host=localhost;Port=5432;Database=panixida;Username=postgres;Password=postgres"
  }
}
```

### Register EF Infrastructure

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.DbContexts;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.DependencyInjection;

public static class PersistenceRegistration
{
    public static IServiceCollection AddPersistence(
        IServiceCollection services,
        IConfiguration configuration)
    {
        return services.AddPostgreSqlEfRepository<AppWriteDbContext, AppReadDbContext>(
            configuration);
    }
}

public sealed class AppWriteDbContext(
    DbContextOptions<AppWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<AppWriteDbContext>(options, interceptors)
{
}

public sealed class AppReadDbContext(
    DbContextOptions<AppReadDbContext> options)
    : ReadDbContext<AppReadDbContext>(options)
{
}
```

By default, a DbContext uses the provider's default schema for its tables, and a write DbContext keeps `__EFMigrationsHistory` there as well. Override `UseContextNameAsSchema` to derive the table schema from the context type name. For a write DbContext, its migration history follows the same schema:

```csharp
public sealed class OrdersWriteDbContext(
    DbContextOptions<OrdersWriteDbContext> options,
    IEnumerable<IInterceptor> interceptors)
    : WriteDbContext<OrdersWriteDbContext>(options, interceptors)
{
    protected override bool UseContextNameAsSchema => true;
}

public sealed class OrdersReadDbContext(
    DbContextOptions<OrdersReadDbContext> options)
    : ReadDbContext<OrdersReadDbContext>(options)
{
    protected override bool UseContextNameAsSchema => true;
}

services.AddPostgreSqlEfRepository<OrdersWriteDbContext, OrdersReadDbContext>(
    configuration);
```

The `WriteDbContext`, `ReadDbContext`, and `DbContext` suffixes are removed before conversion to snake_case, so both contexts above use the `orders` schema. Only write DbContexts configure migration history; read DbContexts currently configure table mapping only and are not migration owners.

Use `AddPostgreSqlWriteEfRepository<TWriteDbContext>` when the application only needs write-side infrastructure, or `AddPostgreSqlReadEfRepository<TReadDbContext>` when it only needs read-side infrastructure.
The registration methods scan DbContext assemblies and register concrete repository implementations as scoped services for non-generic application contracts derived from `IRepository<TId, TAggregateRoot>` or `IReadRepository<TId>`.
Write repository implementations are discovered from the write DbContext assembly, and read repository implementations are discovered from the read DbContext assembly.

Each write registration exposes its `IUnitOfWork` under the write `DbContext` type as a keyed service:

```csharp
var unitOfWork = serviceProvider.GetRequiredKeyedService<IUnitOfWork>(
    typeof(AppWriteDbContext));
```

Persistence infrastructure does not register a non-keyed `IUnitOfWork`. A host-level mediator or messaging runtime can expose its own non-keyed proxy that resolves the keyed Unit of Work for the active module.

## Usage

### Write Repository

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Domain.AggregateRoots;
using PANiXiDA.Core.Domain.Identifiers;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

public readonly record struct OrderId(Guid Value) : IStronglyTypedId;

public sealed class Order(OrderId id) : AggregateRoot<OrderId>(id)
{
    public string Number { get; private set; } = string.Empty;
}

public sealed class OrderConfiguration : AuditableEntityConfiguration<Order>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Order> builder)
    {
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id)
            .HasConversion(id => id.Value, value => new OrderId(value));
        builder.Property(order => order.Number).HasMaxLength(64).IsRequired();
    }
}

public interface IOrderRepository : IRepository<OrderId, Order>
{
}

public sealed class OrderRepository(
    AppWriteDbContext dbContext,
    IAggregateTracker aggregateTracker)
    : EfRepository<AppWriteDbContext, OrderId, Order>(dbContext, aggregateTracker), IOrderRepository
{
}
```

`EfRepository` persists aggregate roots through `AddAsync`, `UpdateAsync`, and `DeleteAsync`, and tracks touched aggregate roots through `IAggregateTracker`. The built-in write registration adds the generic `AggregateTracker` implementation as scoped, but consumers can register their own tracker before calling the EF registration methods. When a unit-of-work transaction is active, repository saves participate in that transaction and `EfUnitOfWork` commits or rolls it back.

### Read Models

```csharp
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

public sealed class OrderReadDbModel : AuditableReadDbModel<Guid>
{
    public string Number { get; set; } = string.Empty;
}

public sealed record OrderReadModel(Guid Id, string Number);

public sealed class OrderReadModelMapper
    : IReadModelMapper<Guid, OrderReadDbModel, OrderReadModel>
{
    public static IQueryable<OrderReadModel> ProjectTo(IQueryable<OrderReadDbModel> query)
    {
        return query.Select(order => new OrderReadModel(order.Id, order.Number));
    }
}
```

Concrete `ReadDbModel<TId>` types in the read DbContext assembly are registered automatically. By default they are mapped as no-tracking models and excluded from migrations, which is useful when read models point to tables or views owned by another context.

### Read Repository

```csharp
using PANiXiDA.Core.Application.Persistence;
using PANiXiDA.Core.Application.Querying.Pagination;
using PANiXiDA.Core.Application.Querying.Sorting;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read;

public interface IOrderReadRepository : IReadRepository<Guid>
{
    Task<OrderReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<PaginationResult<OrderReadModel>> GetPageAsync(
        PaginationParameters pagination,
        SortParameters sort,
        CancellationToken cancellationToken);
}

public sealed class OrderReadRepository(AppReadDbContext dbContext)
    : EfReadRepository<AppReadDbContext, Guid, OrderReadDbModel>(dbContext), IOrderReadRepository
{
    public Task<OrderReadModel?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return GetByIdAsync<OrderReadModel, OrderReadModelMapper>(id, cancellationToken);
    }

    public Task<PaginationResult<OrderReadModel>> GetPageAsync(
        PaginationParameters pagination,
        SortParameters sort,
        CancellationToken cancellationToken)
    {
        return GetPagedResultAsync<OrderReadModel, OrderReadModelMapper>(
            Query,
            pagination,
            sort,
            cancellationToken);
    }
}
```

## Behavior Notes

- Audit timestamps are stored as EF Core shadow properties for write entities configured through `AuditableEntityConfiguration<TEntity>`.
- Added entities receive `CreatedAt` and `UpdatedAt`.
- Modified entities receive a new `UpdatedAt`; `CreatedAt` is marked as not modified.
- Deleted entities that have `DeletedAt` are converted to modified entities and receive `DeletedAt` and `UpdatedAt`.
- `AuditableReadDbModel<TId>` and auditable write configurations apply a query filter that hides rows where `DeletedAt` is not null.
- `EfReadRepository` uses dynamic sorting field names; callers should pass known model property names, not arbitrary user input without validation.
- Repository implementation scanning registers concrete, non-abstract, non-generic classes against non-generic contracts that inherit `IRepository<TId, TAggregateRoot>` or `IReadRepository<TId>`. Direct base generic repository interfaces are intentionally ignored.

## Project Structure

```text
.
|-- src/
|   `-- PANiXiDA.Core.Infrastructure.Persistence.Ef/
|-- tests/
|   |-- PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests/
|   `-- PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests/
|-- .github/workflows/ci.yml
|-- Directory.Build.props
|-- Directory.Build.targets
|-- Directory.Packages.props
|-- global.json
|-- version.json
|-- LICENSE
`-- README.md
```

## Development

### Build

```bash
dotnet restore
dotnet build --configuration Release
```

### Format

```bash
dotnet format
```

### Test

```bash
dotnet test --configuration Release
```

Integration tests start a PostgreSQL container through Testcontainers. Docker must be running before executing the full test suite.

To run only unit tests:

```bash
dotnet test tests/PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests/PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.csproj --configuration Release
```

To run only integration tests:

```bash
dotnet test tests/PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests/PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.csproj --configuration Release
```

### Test With Coverage

```bash
dotnet test --configuration Release --coverage --coverage-output-format cobertura --coverage-output coverage.cobertura.xml
```

### Pack

```bash
dotnet pack --configuration Release
```

### Full Local Validation

```bash
dotnet restore
dotnet format
dotnet build --configuration Release
dotnet test --configuration Release
dotnet pack --configuration Release
```

### Continuous integration

Pull requests from branches in this repository run formatting, tests, and
SonarQube analysis. Publishing from `main` starts only after the SonarQube
Quality Gate succeeds.

Before enabling this workflow, add the repository to the
[shared SonarQube inventory](https://github.com/panixida-infrastructure/core-platform/blob/main/inventory/sonarqube/repositories.json).
Reconciliation provisions the `SONAR_PROJECT_KEY` repository variable and the
`SONAR_TOKEN` repository secret; `SONAR_HOST_URL` is configured at the
organization level. GitHub does not expose repository secrets to pull requests
from forks, so SonarQube analysis is skipped for those pull requests while
formatting and tests continue to run.

## Tooling and Conventions

This repository uses:

- .NET 10
- Nullable enabled
- Implicit usings enabled
- Central package management
- Microsoft Testing Platform
- xUnit v3
- FluentAssertions
- Testcontainers for PostgreSQL integration tests
- Nerdbank.GitVersioning

## License

This project is licensed under the Apache-2.0 license.

See the [LICENSE](LICENSE) file for details.

## Maintainers

Maintained by PANiXiDA.

For questions or improvements, use GitHub Issues or Pull Requests.
