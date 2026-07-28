using PANiXiDA.Core.Domain.Abstractions;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Repositories.Interfaces;

internal interface IAssemblyWriteRepository : IRepository<TestAggregateRootId, TestAggregateRoot>
{
}
