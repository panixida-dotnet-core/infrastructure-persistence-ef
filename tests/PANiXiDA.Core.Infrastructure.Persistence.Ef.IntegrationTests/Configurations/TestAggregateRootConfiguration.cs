using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;
using PANiXiDA.Core.Infrastructure.Persistence.Ef.Write;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Configurations;

internal sealed class TestAggregateRootConfiguration : AuditableEntityConfiguration<TestAggregateRoot>
{
    protected override void ConfigureEntity(EntityTypeBuilder<TestAggregateRoot> builder)
    {
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id)
            .HasConversion(id => id.Value, value => new TestAggregateRootId(value));
        builder.Property(item => item.Name);
    }
}
