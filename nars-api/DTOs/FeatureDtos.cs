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
    [param: Required] string Type,
    [param: Required] string Layer,
    [param: Required] string Label,
    [property: JsonRequired] JsonElement Data
);

/// <summary>
/// Request body for updating an existing feature. Only non-null fields
/// will be applied — partial updates are supported.
/// </summary>
public record FeatureUpdateRequest(
    string? Label,
    JsonElement? Data
);

/// <summary>
/// A single layer option in the feature type definition response.
/// </summary>
public record LayerOption(
    string Key,
    string Label,
    string? Category = null
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

public record LoadFeaturesResponse(
    object Features,
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

public record ActionResponse(
    bool Success,
    string? Message = null
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

public record DetailResponse(
    string Detail
);
