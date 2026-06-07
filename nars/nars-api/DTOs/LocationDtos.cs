using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

public record WilayaItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name_fr")] string NameFr,
    [property: JsonPropertyName("name_ar")] string NameAr,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude
);

public record DairaItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name_fr")] string NameFr,
    [property: JsonPropertyName("name_ar")] string NameAr,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("full_name")] string FullName
);

public record CommuneItem(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name_fr")] string NameFr,
    [property: JsonPropertyName("name_ar")] string NameAr,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("latitude")] double? Latitude,
    [property: JsonPropertyName("longitude")] double? Longitude,
    [property: JsonPropertyName("full_name")] string FullName
);
