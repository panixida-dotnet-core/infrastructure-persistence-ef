using System.Runtime.CompilerServices;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

using PANiXiDA.Core.Domain.AggregateRoots;

namespace PANiXiDA.Core.Infrastructure.Persistence.Ef.Tracking;

internal static class DeletedAggregateStore
{
    private static readonly ConditionalWeakTable<DbContext, HashSet<IAggregateRoot>> Aggregates = new();

    public static HashSet<IAggregateRoot> Get(DbContext context)
    {
        return Aggregates.GetValue(context, _ => new HashSet<IAggregateRoot>(ReferenceEqualityComparer.Instance));
    }

    public static EntityEntry<IAggregateRoot>[] GetDeletedEntries(DbContext context)
    {
        return [.. context.ChangeTracker.Entries<IAggregateRoot>().Where(entry => entry.State == EntityState.Deleted)];
    }

    public static void Retain(DbContext context, IEnumerable<EntityEntry<IAggregateRoot>> entries)
    {
        foreach (var entry in entries)
        {
            if (entry.State is EntityState.Deleted or EntityState.Detached)
            {
                Get(context).Add(entry.Entity);
            }
        }
    }
}
