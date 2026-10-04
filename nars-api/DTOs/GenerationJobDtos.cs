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

    /// <summary>
    /// When true the job runs the districts phase after the roads acceptance:
    /// the worker partitions the commune's urban areas along its boulevards and
    /// avenues and writes the pieces as pending district drafts. The phase needs
    /// no imagery of its own — it reads the areas and roads already mapped.
    /// Nullable so an older client that omits the field is not a breaking
    /// change: absent means false, i.e. roads-only as before.
    /// </summary>
    public bool? GenerateDistricts { get; set; }
}

/// <summary>
/// Body for POST /api/generation/districts — the districts phase on its own,
/// with no imagery and no road run.
/// </summary>
/// <remarks>
/// The phase needs no satellite tiles of its own: it partitions the commune's
/// urban areas along the boulevards and avenues already mapped, entirely in
/// PostGIS. It used to be reachable only as the tail of a generation job
/// (<see cref="CreateGenerationJobRequest.GenerateDistricts"/>), which forced a
/// full re-segmentation of the commune just to re-cut districts. This endpoint
/// runs the pass directly so the districts phase is independent of the roads
/// phase.
/// </remarks>
public sealed class GenerateDistrictsRequest
{
    [Required]
    public int? CommuneId { get; set; }
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
/// <summary>A district draft created by the districts phase.</summary>
public sealed record GeneratedDistrictDto(Guid DraftId, double AreaM2, double Lat, double Lng);

/// <summary>
/// Districts-phase outcome stored in generation_jobs.districts_result. The
/// drafts are rows in the review queue (ai_draft_features), not districts yet —
/// a reviewer accepts each one to materialize it.
/// </summary>
public sealed record GenerateDistrictsResponse(
    IReadOnlyList<GeneratedDistrictDto> Districts,
    int AbsorbedSlivers,
    int PrimaryRoadCount,
    int UrbanAreaCount);

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
    JsonElement? Result,
    bool GenerateDistricts = false,
    JsonElement? DistrictsResult = null);
