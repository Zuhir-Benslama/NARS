using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record FieldInspectRequest(
    [property: JsonPropertyName("feature_id")][param: Required] string FeatureId,
    [property: JsonPropertyName("type")][param: Required][param: MaxLength(30)] string Type,
    [property: JsonPropertyName("data")][property: JsonRequired][param: Required] JsonNode Data,
    [property: JsonPropertyName("status")][param: Required][param: MaxLength(20)] string Status
);

public record FieldEntranceCreateRequest(
    [property: JsonPropertyName("road_id")][param: Required] string RoadId,
    [property: JsonPropertyName("data")][property: JsonRequired][param: Required] JsonNode Data,
    [property: JsonPropertyName("label")][param: MaxLength(500)] string? Label
);

public record FieldInspectionResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("feature_id")] string FeatureId,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("data")] JsonNode? Data,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("created_at")] DateTime CreatedAt
);

public record FieldInspectionsResponse(
    [property: JsonPropertyName("inspections")] IReadOnlyList<FieldInspectionResponse> Inspections
);
