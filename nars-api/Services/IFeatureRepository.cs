using System.Text.Json;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public interface IFeatureRepository
{
    Task<bool> RoadExistsAsync(Guid roadId, Guid userId, CancellationToken ct);
    Task<Guid> SaveFeatureAsync(FeatureBase entity, string featureType, CancellationToken ct);
    Task<string?> GetFeatureTypeAsync(Guid featureId, CancellationToken ct);
    Task<bool> OwnsFeatureAsync(Guid featureId, string featureType, Guid userId, CancellationToken ct);
    Task<bool> UpdateFeatureAsync(FeatureTypeDescriptor descriptor, Guid featureId, Guid userId, FeatureUpdateRequest body, DateTime updatedAt, CancellationToken ct);
    Task<bool> DeleteFeatureAsync(Guid featureId, Guid userId, string featureType, CancellationToken ct);
    Task<(int total, List<Guid> ids)> ClearAllFeaturesAsync(Guid userId, CancellationToken ct);
}
