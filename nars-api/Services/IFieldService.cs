using System.Text.Json;

namespace NarsApi.Services;

public interface IFieldService
{
    Task<(List<object> Items, int Total)> QueryFeaturesAsync(string tableName, Guid[] userIds, int skip, int take, CancellationToken ct = default);
    Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId, CancellationToken ct = default);
}
