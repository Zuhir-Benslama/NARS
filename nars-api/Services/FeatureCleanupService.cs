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
        var descriptors = FeatureTypeRegistry.GetAllDescriptors();

        // Collect all matching feature IDs across every feature table in a single
        // pass so the registry cleanup is one DELETE instead of one per table.
        var allFeatureIds = new List<Guid>();
        foreach (var descriptor in descriptors)
        {
            var dbSet = descriptor.GetDbSet(db);
            var ids = await dbSet.Where(f => f.UserId == userId).Select(f => f.Id).ToListAsync(ct);
            allFeatureIds.AddRange(ids);
        }

        if (allFeatureIds.Count == 0)
        {
            return 0;
        }

        // Single registry DELETE for all feature types.
        await db.FeatureRegistry
            .Where(r => allFeatureIds.Contains(r.Id))
            .ExecuteDeleteAsync(ct);

        // One DELETE per feature table.
        var total = 0;
        foreach (var descriptor in descriptors)
        {
            var dbSet = descriptor.GetDbSet(db);
            total += await dbSet.Where(f => f.UserId == userId).ExecuteDeleteAsync(ct);
        }

        return total;
    }
}
