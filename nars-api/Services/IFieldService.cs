using NarsApi.Infrastructure;

namespace NarsApi.Services;

public interface IFieldService
{
    Task<(List<object> Items, int Total)> QueryFeaturesAsync(FeatureTypeDescriptor descriptor, int communeId, int skip, int take, CancellationToken ct = default);
    Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId, CancellationToken ct = default);
}
