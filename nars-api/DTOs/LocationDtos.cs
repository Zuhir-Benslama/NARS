using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

/// <summary>
/// Shared base for the administrative hierarchy list items so the identical
/// id/name_fr/name_ar/latitude/longitude fields are declared once. Derived
/// records add only their context-specific fields, so each serializes with the
/// exact same JSON keys as before.
/// </summary>
public record GeoItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name_fr")] string NameFr,
    [property: JsonPropertyName("name_ar")] string NameAr,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude
);

public record WilayaItem(
    int Id, string NameFr, string NameAr, double? Latitude, double? Longitude)
    : GeoItem(Id, NameFr, NameAr, Latitude, Longitude);

public record DairaItem(
    int Id, string NameFr, string NameAr, double? Latitude, double? Longitude,
    [property: JsonPropertyName("full_name")] string FullName)
    : GeoItem(Id, NameFr, NameAr, Latitude, Longitude);

public record CommuneItem(
    int Id, string NameFr, string NameAr,
    [property: JsonPropertyName("code")] string? Code,
    double? Latitude, double? Longitude,
    [property: JsonPropertyName("full_name")] string FullName)
    : GeoItem(Id, NameFr, NameAr, Latitude, Longitude);

public record CommuneBoundaryResponse(
    [property: JsonPropertyName("communeId")] int CommuneId,
    [property: JsonPropertyName("communeName")] string? CommuneName,
    [property: JsonPropertyName("geometry")] string Geometry
);
