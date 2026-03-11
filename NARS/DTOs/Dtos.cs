using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NarsApi.DTOs;

// ── Auth ──────────────────────────────────────────────────

public record SignUpRequest(
    [Required] string Name,
    [Required, EmailAddress] string Email,
    [Required] string Phone,
    [Required] string Username,
    [Required] string Password,
    [Required] int CommuneId
);

public record SignInRequest(
    [Required] string Username,
    [Required] string Password
);

// ── Features ──────────────────────────────────────────────

public record FeatureSaveRequest(
    [Required] string Type,
    [Required] string Layer,
    [Required] string Label,
    [Required] JsonElement Data
);

public record FeatureUpdateRequest(
    [property: JsonPropertyName("label")] string? Label,
    [property: JsonPropertyName("data")]  JsonElement? Data
);

// ── Validation ────────────────────────────────────────────

/// <summary>A single lat/lng coordinate pair.</summary>
public record CoordDto(
    [property: JsonPropertyName("lat")] double Lat,
    [property: JsonPropertyName("lng")] double Lng
);

/// <summary>POST /api/validate/road</summary>
public record ValidateRoadRequest(
    [Required]
    [property: JsonPropertyName("coordinates")]
    List<CoordDto> Coordinates
);

public record ValidateRoadResponse(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("error")] string? Error
);

/// <summary>POST /api/validate/district</summary>
public record ValidateDistrictRequest(
    [Required]
    [property: JsonPropertyName("coordinates")]
    List<CoordDto> Coordinates,
    [property: JsonPropertyName("districtTypeKey")]
    string? DistrictTypeKey
);

public record ValidateDistrictResponse(
    [property: JsonPropertyName("valid")] bool Valid,
    [property: JsonPropertyName("error")] string? Error
);

/// <summary>GET /api/validate/districts/coverage</summary>
public record DistrictCoverageResponse(
    [property: JsonPropertyName("covered")] bool Covered,
    [property: JsonPropertyName("message")] string Message
);

/// <summary>POST /api/road-side</summary>
public record RoadSideRequest(
    [Required][property: JsonPropertyName("roadId")]  int RoadId,
    [Required][property: JsonPropertyName("lat")]     double Lat,
    [Required][property: JsonPropertyName("lng")]     double Lng
);

public record RoadSideResponse(
    [property: JsonPropertyName("side")]            string Side,
    [property: JsonPropertyName("suggestedNumber")] int SuggestedNumber
);

/// <summary>POST /api/areas/refresh-scattered</summary>
public record ScatteredRefreshResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("geojson")] string? GeoJson,
    [property: JsonPropertyName("message")] string Message
);

// ── Response shapes ───────────────────────────────────────

public record LocationInfo(int? Id, string? NameFr, string? NameAr, double? Latitude, double? Longitude);
public record WilayaDto(int Id, string NameFr, string NameAr, double? Latitude, double? Longitude);
public record DairaDto(int Id, string NameFr, string NameAr, double? Latitude, double? Longitude, string? FullName);
public record CommuneDto(int Id, string NameFr, string NameAr, int? Code, double? Latitude, double? Longitude, string? FullName);
public record FeatureDto(int Id, string Type, string Layer, string Label, JsonElement Data, string? CreatedAt);

// ── Feature-type hierarchy DTOs ───────────────────────────

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
