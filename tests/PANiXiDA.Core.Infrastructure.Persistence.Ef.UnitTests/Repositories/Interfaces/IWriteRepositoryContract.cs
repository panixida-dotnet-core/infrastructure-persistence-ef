using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.UnitTests.Repositories.Interfaces;

internal interface IWriteRepositoryContract : IRepository<int, TestAggregateRoot>
{
}
