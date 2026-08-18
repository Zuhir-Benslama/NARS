using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

/// <summary>
/// Handles bulk deletion of features across all registered feature tables.
/// </summary>
public sealed class FeatureCleanupService : IFeatureCleanupService
{
    public async Task<int> DeleteAllFeaturesForUserAsync(AppDbContext db, Guid userId, CancellationToken ct)
    {
        var total = 0;

        foreach (var descriptor in FeatureTypeRegistry.GetAllDescriptors())
        {
            var dbSet = descriptor.GetDbSet(db);

            // Remove the feature-registry entries via a subquery so no IDs are
            // materialized into memory, then delete the feature rows.
            await db.FeatureRegistry
                .Where(r => dbSet.Where(f => f.UserId == userId).Select(f => f.Id).Contains(r.Id))
                .ExecuteDeleteAsync(ct);

            total += await dbSet.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
        }

        return total;
    }
}
