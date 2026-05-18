using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Configurations;

internal sealed class NoSoftDeleteAggregateRootConfiguration
    : AuditableEntityConfiguration<NoSoftDeleteAggregateRoot>
{
    protected override void ConfigureEntity(EntityTypeBuilder<NoSoftDeleteAggregateRoot> builder)
    {
        builder.HasKey(item => item.Id);
    }

    protected override bool IsSoftDeleteEnabled()
    {
        return false;
    }
}
