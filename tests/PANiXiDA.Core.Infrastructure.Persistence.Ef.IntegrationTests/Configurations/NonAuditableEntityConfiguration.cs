using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Configurations;

internal sealed class NonAuditableEntityConfiguration : IEntityTypeConfiguration<NonAuditableEntity>
{
    public void Configure(EntityTypeBuilder<NonAuditableEntity> builder)
    {
        builder.HasKey(item => item.Id);
    }
}
