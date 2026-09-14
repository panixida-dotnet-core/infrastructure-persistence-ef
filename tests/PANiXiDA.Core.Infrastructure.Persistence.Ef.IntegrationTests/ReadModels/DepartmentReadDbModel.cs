using PANiXiDA.Core.Infrastructure.Persistence.Ef.Read.Models;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.ReadModels;

internal sealed class DepartmentReadDbModel : ReadDbModel<int>
{
    public string Name { get; set; } = string.Empty;
    public int Rank { get; set; }
}
