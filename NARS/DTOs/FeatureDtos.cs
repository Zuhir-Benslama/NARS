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
    [property: JsonPropertyName("data")][param: Required][property: JsonRequired] JsonElement Data
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
