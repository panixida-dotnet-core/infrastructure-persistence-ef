namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.Entities;

internal sealed class ConvertedAuditEntity
{
    public Guid Id { get; init; }

    public AuditCreation Creation { get; set; } = null!;

    public AuditUpdate Update { get; private set; }

    public AuditDeletion? Deletion { get; private set; }

    public string Name { get; set; } = string.Empty;
}

internal sealed record AuditCreation(DateTime Value);

internal readonly record struct AuditUpdate(DateTime Value);

internal readonly record struct AuditDeletion(DateTime Value);
