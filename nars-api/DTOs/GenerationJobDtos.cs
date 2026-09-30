using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace NarsApi.DTOs;

/// <summary>
/// One grid tile of a generation job, as computed by the client's
/// satellite-tiler split (zoom-18 tiles covering an urban bounding box).
/// </summary>
public sealed record GenerationGridDto(
    string ChunkKey,
    int Zoom,
    int X0,
    int Y0,
    int Width,
    int Height,
    double MinLon,
    double MinLat,
    double MaxLon,
    double MaxLat);

/// <summary>
/// Body for POST /api/generation/jobs. <see cref="CommuneId"/> is the target
/// commune; <see cref="Grids"/> must mirror exactly one grid split of the
/// urban bounding box (the client already validated its own split).
/// </summary>
public sealed class CreateGenerationJobRequest
{
    [Required]
    public int? CommuneId { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(4096)]
    public List<GenerationGridDto> Grids { get; set; } = [];
}

/// <summary>
/// Multipart form body for POST /api/generation/jobs/{id}/chunks/{chunkId}/raster.
/// Bounds are NOT part of the upload: they come from the grid the client
/// declared when creating the job.
/// </summary>
public sealed class JobRasterUploadRequest
{
    [Required]
    public IFormFile Raster { get; set; } = null!;
}

/// <summary>
/// Client-visible chunk state for the job detail endpoint.
/// </summary>
public sealed record GenerationChunkView(
    Guid Id,
    string ChunkKey,
    int Zoom,
    int X0,
    int Y0,
    int Width,
    int Height,
    double? MinLon,
    double? MinLat,
    double? MaxLon,
    double? MaxLat,
    string Status,
    int Attempts,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Client-visible job state. The UI polls this to drive the progress bar and
/// per-chunk list; <see cref="Result"/> holds the GenerateRoadsResponse JSON
/// (dropped/created/breakdown) once the job reaches <c>done</c>.
/// </summary>
public sealed record GenerationJobView(
    Guid Id,
    int CommuneId,
    string Status,
    string? Stage,
    int TotalChunks,
    int DoneChunks,
    double Progress,
    IReadOnlyList<Guid> DraftIds,
    string? Error,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<GenerationChunkView> Chunks,
    JsonElement? Result);
