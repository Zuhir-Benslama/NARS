using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record RoadSideRequest(
    [Required][property: JsonPropertyName("roadId")][property: JsonRequired] Guid RoadId,
    [Required][property: JsonPropertyName("lat")][property: JsonRequired][property: Range(-90.0, 90.0)] double Lat,
    [Required][property: JsonPropertyName("lng")][property: JsonRequired][property: Range(-180.0, 180.0)] double Lng
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
