using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record RoadSideRequest(
    [Required][property: JsonPropertyName("roadId")][property: JsonRequired] Guid RoadId,
    [Required][property: JsonPropertyName("lat")][property: JsonRequired] double Lat,
    [Required][property: JsonPropertyName("lng")][property: JsonRequired] double Lng
);

public record RoadSideResponse(
    [property: JsonPropertyName("side")] string Side,
    [property: JsonPropertyName("suggestedNumber")] int SuggestedNumber
);

public record ScatteredRefreshResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("geojson")] string? GeoJson,
    [property: JsonPropertyName("message")] string Message
);
