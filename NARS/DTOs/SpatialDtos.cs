using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record RoadSideRequest(
    [Required][property: JsonPropertyName("roadId")] int    RoadId,
    [Required][property: JsonPropertyName("lat")]    double Lat,
    [Required][property: JsonPropertyName("lng")]    double Lng
);

public record RoadSideResponse(
    [property: JsonPropertyName("side")]            string Side,
    [property: JsonPropertyName("suggestedNumber")] int    SuggestedNumber
);

public record ScatteredRefreshResponse(
    [property: JsonPropertyName("success")] bool    Success,
    [property: JsonPropertyName("geojson")] string? GeoJson,
    [property: JsonPropertyName("message")] string  Message
);

public record LocationInfo(int? Id, string? NameFr, string? NameAr, double? Latitude, double? Longitude);
public record WilayaDto(int Id, string NameFr, string NameAr, double? Latitude, double? Longitude);
public record DairaDto(int Id, string NameFr, string NameAr, double? Latitude, double? Longitude, string? FullName);
public record CommuneDto(int Id, string NameFr, string NameAr, int? Code, double? Latitude, double? Longitude, string? FullName);
