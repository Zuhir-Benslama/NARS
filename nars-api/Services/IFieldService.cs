using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

public interface IFieldService
{
    /// <summary>Queries features of a given type within a commune with pagination.</summary>
    Task<(List<FieldFeatureResult> Items, int Total)> QueryFeaturesAsync(FeatureTypeDescriptor descriptor, int communeId, int skip, int take, CancellationToken ct = default);
    /// <summary>Returns the owner user ID and commune ID for a feature.</summary>
    Task<(Guid UserId, int? CommuneId)?> GetFeatureOwnerAsync(string featureType, Guid featureId, CancellationToken ct = default);
    /// <summary>Returns inspections for a feature, newest first, with pagination.</summary>
    Task<List<FieldInspectionResponse>> GetInspectionsAsync(Guid featureId, int skip, int take, CancellationToken ct = default);
    /// <summary>Looks up a road and its owner's commune for entrance creation.</summary>
    Task<(Guid OwnerUserId, int? CommuneId)?> GetRoadOwnerAsync(Guid roadId, CancellationToken ct = default);
    /// <summary>Creates a house entrance linked to a road within a transaction.</summary>
    Task<Guid> CreateEntranceAsync(Guid roadId, Guid ownerUserId, Guid creatorUserId, string label, string data, CancellationToken ct = default);
    /// <summary>Looks up a feature registry entry.</summary>
    Task<string?> GetFeatureRegistryTypeAsync(Guid featureId, CancellationToken ct = default);
    /// <summary>Inspects a feature and persists the inspection record.</summary>
    Task<Guid> SubmitInspectionAsync(Guid featureId, Guid userId, string type, string status, string data, CancellationToken ct = default);
}
