using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

/// <summary>
/// Request body for deleting all user features. Requires explicit confirmation.
/// </summary>
public record ClearFeaturesRequest(
    [property: JsonRequired] bool Confirm
);

/// <summary>
/// Request body for saving a new feature. The Data field contains
/// GeoJSON-like coordinate data as a raw JsonElement.
/// </summary>
public record FeatureSaveRequest(
    [param: Required][param: MaxLength(30)] string Type,
    [param: Required][param: MaxLength(50)] string Layer,
    [param: Required][param: MaxLength(500)] string Label,
    [property: JsonRequired] JsonElement Data
);

/// <summary>
/// Request body for updating an existing feature. Only non-null fields
/// will be applied — partial updates are supported.
/// </summary>
public record FeatureUpdateRequest(
    [param: MaxLength(500)] string? Label,
    JsonElement? Data
);

public record LayerOption(
    string Key,
    string Label,
    string? Category = null
);

/// <summary>
/// Result row returned by <see cref="Infrastructure.FeatureQueryHelper"/>.
/// Used instead of anonymous types to preserve type safety.
/// </summary>
public record FeatureResult(
    string Id,
    string Type,
    string? Layer,
    string? Label,
    JsonElement Data,
    string CreatedAt
);

/// <summary>
/// Complete feature type definition (e.g. "area" with its layers).
/// </summary>
public record FeatureTypeDefinition(
    string Key,
    string Label,
    string Icon,
    IReadOnlyList<LayerOption> Layers
);

// ─── RESPONSE DTOs ────────────────────────────────────────────────────────────

public record SaveFeatureResponse(
    bool Success,
    string Id,
    string Message
);

public record LoadFeaturesResponse<T>(
    IReadOnlyList<T> Features,
    int Count,
    int Skip,
    int Take
);

public record FeatureStatsResponse(
    long Area,
    long District,
    long CityCenter,
    long Road,
    long HouseEntrance,
    long PublicBuilding,
    long PublicSpace,
    long NamingPanel,
    long Total
);

public record ScatteredStatusResponse(
    string? LastErrorTime,
    string? LastErrorMessage,
    bool HasError
);

public record UpdateFeatureResponse(
    bool Success,
    string Id,
    DateTime UpdatedAt
);

public record UpdateCredentialsResponse(
    bool Success,
    string Message,
    UserCredentialsInfo? User = null
);

public record UserCredentialsInfo(
    string? Username,
    string? Email
);

public record FieldFeatureResult(
    string Id,
    string UserId,
    string Layer,
    string Label,
    JsonElement? Data,
    DateTime CreatedAt,
    DateTime? UpdatedAt
);

