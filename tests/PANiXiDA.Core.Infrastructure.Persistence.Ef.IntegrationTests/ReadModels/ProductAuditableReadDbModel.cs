using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;

internal sealed class ProductAuditableReadDbModel : AuditableReadDbModel<int>
{
    public string Name { get; set; } = string.Empty;
}
