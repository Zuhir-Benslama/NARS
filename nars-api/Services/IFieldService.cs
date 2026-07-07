using NarsApi.DTOs;
using NarsApi.Infrastructure;

namespace NarsApi.Services;

public interface IFieldService
{
    /// <summary>Queries features of a given type within a commune with pagination.</summary>
    Task<(List<FieldFeatureResult> Items, int Total)> QueryFeaturesAsync(FeatureTypeDescriptor descriptor, int communeId, int skip, int take, CancellationToken ct = default);
    /// <summary>Returns the owner user ID and commune ID for a feature.</summary>
    Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId, CancellationToken ct = default);
}
