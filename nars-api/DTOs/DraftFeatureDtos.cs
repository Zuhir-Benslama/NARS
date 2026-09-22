using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using NarsApi.Services;

namespace NarsApi.DTOs;

/// <summary>
/// Multipart form body for the draft-features segmentation endpoint.
/// Uses [Required] (form-model-binding compatible) rather than [JsonRequired],
/// which is only honored by the JSON serializer.
/// </summary>
public sealed class SegmentTileRequest
{
    [Required]
    public int? CommuneId { get; set; }

    [Required]
    public IFormFile Tile { get; set; } = null!;

    /// <summary>
    /// Selects which segmentation model/endpoint to call: "building" (default)
    /// or "road". Each request targets exactly one feature type (per-request
    /// separation), so a response carries only buildings or roads, never both.
    /// </summary>
    public string? FeatureType { get; set; }

    [Required]
    public double? MinLon { get; set; }

    [Required]
    public double? MinLat { get; set; }

    [Required]
    public double? MaxLon { get; set; }

    [Required]
    public double? MaxLat { get; set; }
}

public sealed record SegmentSummaryResponse
{
    public int BuildingCount { get; init; }
    public int RoadCount { get; init; }
    public List<Guid> DraftIds { get; init; } = [];
}

public sealed record AiDraftFeatureDto(
    Guid Id,
    string FeatureType,
    string GeometryGeoJson,
    double Confidence,
    string Status,
    DateTimeOffset CreatedAt);

/// <summary>
/// Body for editing a draft's geometry (PUT /api/draft-features/{id}).
/// The geometry must keep the draft feature type's shape (LineString for
/// roads, Polygon for buildings) — enforced in the service.
/// </summary>
public sealed class DraftUpdateRequest
{
    [Required]
    [MinLength(2)]
    public string GeometryGeoJson { get; set; } = string.Empty;
}

/// <summary>
/// Body for POST /api/draft-features/generate-roads. Consumes the road draft
/// ids returned by the segmentation endpoint for the commune; only drafts that
/// satisfy the roads-phase cadastre rules are materialized.
/// </summary>
public sealed class GenerateRoadsRequest
{
    [Required]
    public int? CommuneId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(5000)]
    public List<Guid> DraftIds { get; set; } = [];
}

/// <summary>
/// A road materialized from an AI draft. <see cref="Data"/> is the full
/// production feature payload (type, label, roadTypeKey, lat/lng coordinates)
/// the front-end layer store expects, so it can add the road without a reload.
/// </summary>
public sealed record GeneratedRoadDto(Guid DbId, string Layer, string Label, JsonElement Data);

/// <summary>
/// Result of POST /api/draft-features/generate-roads. Roads that violate the
/// cadastre rules stay pending and are counted in <see cref="Dropped"/>, with a
/// per-rule <see cref="Breakdown"/> of why so the UI can report the reasons.
/// </summary>
public sealed record GenerateRoadsResponse(
    int Dropped,
    IReadOnlyList<GeneratedRoadDto> Created,
    GenerateRoadsDroppedDto Breakdown);

/// <summary>Per-rule drop counts from a generate-roads pass.</summary>
public sealed record GenerateRoadsDroppedDto(
    int TooShort,
    int LowConfidence,
    int ExcessiveTurnAngle,
    int OutsideUrbanArea,
    int InvalidGeometry)
{
    public static GenerateRoadsDroppedDto From(RoadDropBreakdown b)
        => new(b.TooShort, b.LowConfidence, b.ExcessiveTurnAngle, b.OutsideUrbanArea, b.InvalidGeometry);
}
