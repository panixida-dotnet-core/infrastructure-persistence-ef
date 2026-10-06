using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using PANiXiDA.Core.Infrastructure.Persistence.Ef.Constants;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Configurations;

internal sealed class ConvertedAuditEntityConfiguration : AuditableEntityConfiguration<ConvertedAuditEntity>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ConvertedAuditEntity> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Name);
        builder.Property(item => item.Creation)
            .HasConversion(value => value.Value, value => new AuditCreation(value))
            .HasColumnName(EfConstants.CreatedAt);
        builder.Property(item => item.Update)
            .HasConversion(value => value.Value, value => new AuditUpdate(value))
            .HasColumnName(EfConstants.UpdatedAt);
        builder.Property(item => item.Deletion)
            .HasConversion(value => value!.Value.Value, value => new AuditDeletion(value))
            .HasColumnName(EfConstants.DeletedAt);
    }
}
