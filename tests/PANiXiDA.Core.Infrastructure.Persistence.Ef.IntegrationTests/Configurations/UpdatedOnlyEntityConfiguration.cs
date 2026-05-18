using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Configurations;

internal sealed class UpdatedOnlyEntityConfiguration : IEntityTypeConfiguration<UpdatedOnlyEntity>
{
    public void Configure(EntityTypeBuilder<UpdatedOnlyEntity> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name);
        builder.Property<DateTime>(EfConstants.UpdatedAt);
    }
}
