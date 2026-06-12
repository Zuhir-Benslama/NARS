using NarsApi.DTOs;

namespace NarsApi.Services;

public interface IFeatureStatsService
{
    Task<Dictionary<string, long>> GetFeatureCountsAsync(Guid userId, CancellationToken ct = default);
    Task<Dictionary<Guid, UserFeatureStats>> GetUserFeatureCountsAsync(Guid[] userIds, CancellationToken ct = default);
    Task<(List<object> features, int totalCount)> LoadAllFeaturesAsync(Guid userId, int skip, int take, CancellationToken ct = default);
    Task<(List<object> features, int totalCount)> LoadByLayerAsync(Guid userId, string layer, int skip, int take, CancellationToken ct = default);
}
