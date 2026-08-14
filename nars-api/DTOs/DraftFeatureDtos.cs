using System.ComponentModel.DataAnnotations;

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

    [Required]
    public double? MinLon { get; set; }

    [Required]
    public double? MinLat { get; set; }

    [Required]
    public double? MaxLon { get; set; }

    [Required]
    public double? MaxLat { get; set; }
}

public sealed record SegmentSummaryResponse(int BuildingCount, List<Guid> DraftIds);

public sealed record AiDraftFeatureDto(
    Guid Id,
    string FeatureType,
    string GeometryGeoJson,
    double Confidence,
    string Status,
    DateTimeOffset CreatedAt);
