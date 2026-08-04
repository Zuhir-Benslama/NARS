using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public interface IFeatureService
{
    /// <summary>Checks whether a road feature with the given ID exists for the user.</summary>
    Task<bool> RoadExistsAsync(Guid roadId, Guid userId, CancellationToken ct);
    /// <summary>Saves a new feature entity and registers it in the feature type registry.</summary>
    Task<Guid> SaveFeatureAsync(FeatureBase entity, string featureType, CancellationToken ct);
    /// <summary>Returns the feature type string (e.g. "road", "area") for a given feature ID.</summary>
    Task<string?> GetFeatureTypeAsync(Guid featureId, CancellationToken ct);
    /// <summary>Verifies that the user owns the specified feature.</summary>
    Task<bool> OwnsFeatureAsync(Guid featureId, string featureType, Guid userId, CancellationToken ct);
    /// <summary>Updates an existing feature's label and/or data.</summary>
    Task<bool> UpdateFeatureAsync(UpdateFeatureCommand command, CancellationToken ct);
    /// <summary>Deletes a single feature by ID, verifying ownership.</summary>
    Task<bool> DeleteFeatureAsync(Guid featureId, Guid userId, string featureType, CancellationToken ct);
    /// <summary>Deletes all features owned by the user and returns how many were deleted.</summary>
    Task<int> ClearAllFeaturesAsync(Guid userId, CancellationToken ct);
}

public record UpdateFeatureCommand(
    FeatureTypeDescriptor Descriptor,
    Guid FeatureId,
    Guid UserId,
    FeatureUpdateRequest Body,
    DateTime UpdatedAt
);
