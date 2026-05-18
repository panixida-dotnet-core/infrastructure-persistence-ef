namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.IntegrationTests.TestDoubles;

internal sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
{
    public override DateTimeOffset GetUtcNow()
    {
        return utcNow;
    }
}
