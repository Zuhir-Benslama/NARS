using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Infrastructure;

public sealed class UpdatedAtInterceptor(IDateTimeProvider timeProvider) : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            var now = timeProvider.UtcNow;
            foreach (var entry in eventData.Context.ChangeTracker.Entries<FeatureBase>()
                .Where(e => e.State is EntityState.Modified or EntityState.Added))
            {
                entry.Entity.UpdatedAt = now;

                if (entry.State is EntityState.Added && entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }
            }
            foreach (var entry in eventData.Context.ChangeTracker.Entries<Inspection>()
                .Where(e => e.State is EntityState.Modified or EntityState.Added))
            {
                entry.Entity.UpdatedAt = now;

                if (entry.State is EntityState.Added && entry.Entity.CreatedAt == default)
                {
                    entry.Entity.CreatedAt = now;
                }
            }
            foreach (var entry in eventData.Context.ChangeTracker.Entries<User>()
                .Where(e => e.State is EntityState.Added && e.Entity.CreatedAt == default))
            {
                entry.Entity.CreatedAt = now;
            }
            foreach (var entry in eventData.Context.ChangeTracker.Entries<RefreshToken>()
                .Where(e => e.State is EntityState.Added && e.Entity.CreatedAt == default))
            {
                entry.Entity.CreatedAt = now;
            }
        }
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}
