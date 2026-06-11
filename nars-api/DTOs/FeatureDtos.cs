using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

/// <summary>
/// Request body for deleting all user features. Requires explicit confirmation.
/// </summary>
public record ClearFeaturesRequest(
    [property: JsonPropertyName("confirm")][property: JsonRequired] bool Confirm
);

/// <summary>
/// Request body for saving a new feature. The Data field contains
/// GeoJSON-like coordinate data as a raw JsonElement.
/// </summary>
public record FeatureSaveRequest(
    [property: JsonPropertyName("type")][param: Required] string Type,
    [property: JsonPropertyName("layer")][param: Required] string Layer,
    [property: JsonPropertyName("label")][param: Required] string Label,
    [property: JsonPropertyName("data")][property: JsonRequired] JsonElement Data
);

/// <summary>
/// Request body for updating an existing feature. Only non-null fields
/// will be applied — partial updates are supported.
/// </summary>
public record FeatureUpdateRequest(
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("data")] JsonElement? Data
);

/// <summary>
/// A single layer option in the feature type definition response.
/// </summary>
public record LayerOption(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("category")] string? Category = null
);

/// <summary>
/// Complete feature type definition (e.g. "area" with its layers).
/// </summary>
public record FeatureTypeDefinition(
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("label")] string Label,
    [property: JsonPropertyName("icon")] string Icon,
    [property: JsonPropertyName("layers")] IReadOnlyList<LayerOption> Layers
);

// ─── RESPONSE DTOs ────────────────────────────────────────────────────────────

public record SaveFeatureResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("message")] string Message
);

public record LoadFeaturesResponse(
    [property: JsonPropertyName("features")] object Features,
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("skip")] int Skip,
    [property: JsonPropertyName("take")] int Take
);

public record FeatureStatsResponse(
    [property: JsonPropertyName("area")] long Area,
    [property: JsonPropertyName("district")] long District,
    [property: JsonPropertyName("city_center")] long CityCenter,
    [property: JsonPropertyName("road")] long Road,
    [property: JsonPropertyName("house_entrance")] long HouseEntrance,
    [property: JsonPropertyName("public_building")] long PublicBuilding,
    [property: JsonPropertyName("public_space")] long PublicSpace,
    [property: JsonPropertyName("naming_panel")] long NamingPanel,
    [property: JsonPropertyName("total")] long Total
);

public record ScatteredStatusResponse(
    [property: JsonPropertyName("lastErrorTime")] string? LastErrorTime,
    [property: JsonPropertyName("lastErrorMessage")] string? LastErrorMessage,
    [property: JsonPropertyName("hasError")] bool HasError
);

public record UpdateFeatureResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("updated_at")] DateTime UpdatedAt
);

public record ActionResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string? Message = null
);

public record UpdateCredentialsResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("user")] UserCredentialsInfo? User = null
);

public record UserCredentialsInfo(
    [property: JsonPropertyName("username")] string? Username,
    [property: JsonPropertyName("email")] string? Email
);

public record DetailResponse(
    [property: JsonPropertyName("detail")] string Detail
);
