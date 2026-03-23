using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record ClearFeaturesRequest(
    [property: JsonPropertyName("confirm")] bool Confirm
);

public record FeatureSaveRequest(
    [Required] string Type,
    [Required] string Layer,
    [Required] string Label,
    [Required] JsonElement Data
);

public record FeatureUpdateRequest(
    [property: JsonPropertyName("label")] string?       Label,
    [property: JsonPropertyName("data")]  JsonElement?  Data
);

public record FeatureDto(int Id, string Type, string Layer, string Label, JsonElement Data, string? CreatedAt);

public record LayerOption(
    [property: JsonPropertyName("key")]      string Key,
    [property: JsonPropertyName("label")]    string Label,
    [property: JsonPropertyName("category")] string? Category = null
);

public record FeatureTypeDefinition(
    [property: JsonPropertyName("key")]    string Key,
    [property: JsonPropertyName("label")]  string Label,
    [property: JsonPropertyName("icon")]   string Icon,
    [property: JsonPropertyName("layers")] IReadOnlyList<LayerOption> Layers
);
