using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record FieldInspectRequest(
    [property: JsonPropertyName("feature_id")][param: Required] string FeatureId,
    [property: JsonPropertyName("type")][param: Required] string Type,
    [property: JsonPropertyName("data")][param: Required] JsonElement Data,
    [property: JsonPropertyName("status")][param: Required] string Status
);

public record FieldEntranceCreateRequest(
    [property: JsonPropertyName("road_id")][param: Required] string RoadId,
    [property: JsonPropertyName("data")][param: Required] JsonElement Data,
    [property: JsonPropertyName("label")] string? Label
);

public record FieldInspectionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("feature_id")] string FeatureId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] JsonElement? Data,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);
