using NarsApi.Data;

namespace NarsApi.Services;

/// <summary>
/// Handles bulk deletion of features across all registered feature tables.
/// Extracted from FeatureTypeRegistry to separate DB mutation concerns
/// from the static type-mapping registry.
/// </summary>
public interface IFeatureCleanupService
{
    /// <summary>
    /// Deletes all features owned by a user across every registered feature
    /// table plus their <c>feature_registry</c> rows. Callers are responsible
    /// for wrapping this in a transaction.
    /// </summary>
    Task<int> DeleteAllFeaturesForUserAsync(AppDbContext db, Guid userId, CancellationToken ct);
}
