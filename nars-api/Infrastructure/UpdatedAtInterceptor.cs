using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

public sealed class UpdatedAtInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            var now = DateTime.UtcNow;
            foreach (var entry in eventData.Context.ChangeTracker.Entries<FeatureBase>()
                .Where(e => e.State is EntityState.Modified or EntityState.Added))
            {
                entry.Entity.UpdatedAt = now;
            }
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
