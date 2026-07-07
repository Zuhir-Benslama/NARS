using NarsApi.DTOs;

namespace NarsApi.Services;

public interface IFeatureStatsService
{
    /// <summary>Returns feature count broken down by type for a user.</summary>
    Task<Dictionary<string, long>> GetFeatureCountsAsync(Guid userId, CancellationToken ct = default);
    /// <summary>Returns feature counts for multiple users keyed by user ID.</summary>
    Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default);
    /// <summary>Loads all features for a user with pagination.</summary>
    Task<(List<FeatureResult> features, int totalCount)> LoadAllFeaturesAsync(Guid userId, int skip, int take, CancellationToken ct = default);
    /// <summary>Loads features for a user filtered by layer type with pagination.</summary>
    Task<(List<FeatureResult> features, int totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default);
}
