using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

/// <summary>
/// Lifecycle endpoints for async road-generation jobs. The client creates a
/// job with its full grid split, uploads each rendered raster chunk, then
/// polls the job until it reaches <c>done</c> and reads the result (which
/// mirrors <c>POST /api/draft-features/generate-roads</c>). Per-chunk uploads
/// make jobs resumable across browser/pod restarts; the worker pool in
/// nars-api does the segmentation and the roads-phase acceptance.
/// </summary>
[ApiController]
[Route("api/generation/jobs")]
[Authorize]
public sealed class GenerationJobController(
    IGenerationJobService generationJobService,
    ILogger<GenerationJobController> logger,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    // Same accepted raster types as the synchronous segmentation endpoint.
    private static readonly HashSet<string> AllowedTileContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/tiff", "image/tif", "image/jpeg", "image/jpg", "image/png", "image/webp",
    };

    private static readonly HashSet<string> AllowedTileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".jpeg", ".jpg", ".png", ".webp",
    };

    /// <summary>
    /// Creates an async generation job for a commune with its full tile grid.
    /// Chunks start as <c>awaiting_raster</c>; the client uploads each raster
    /// next. The caller must have access to the commune.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<GenerationJobView>> Create(
        [FromBody] CreateGenerationJobRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        if (request.CommuneId is null)
        {
            return Problem(detail: "communeId is required.", statusCode: 400);
        }

        if (request.Grids.Count == 0)
        {
            return Problem(detail: "grids must contain at least one chunk.", statusCode: 400);
        }

        try
        {
            var view = await generationJobService.CreateAsync(
                CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
                RequiredCurrentUserId, request.CommuneId.Value, request.Grids,
                request.GenerateDistricts ?? false, cancellationToken);
            return CreatedAtAction(nameof(Get), new { jobId = view.Id }, view);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "Generation job creation failed: {Reason}", ex.Message);
            return Problem(detail: "The requested commune was not found.", statusCode: 404);
        }
        catch (ArgumentException ex)
        {
            return Problem(detail: ex.Message, statusCode: 400);
        }
    }

    /// <summary>
    /// Current job state — used by the UI to poll progress and, once done, to
    /// read the GenerateRoadsResponse result. The caller must have access to
    /// the job's commune.
    /// </summary>
    [HttpGet("{jobId:guid}")]
    public async Task<ActionResult<GenerationJobView>> Get(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = await generationJobService.GetViewAsync(
                jobId, CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId, cancellationToken);
            return Ok(view);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "Unknown generation job: {JobId}", jobId);
            return Problem(detail: "The generation job was not found.", statusCode: 404);
        }
    }

    /// <summary>
    /// Uploads one rendered raster chunk for the job. Rasters are accepted
    /// exactly once per chunk (first upload wins / subsequent uploads 409);
    /// a client retry on a failed request must create a new job. Validates the
    /// same image types the synchronous segmentation endpoint accepts, under a
    /// 50MB cap (a rendered zoom-18 tile is well below that). Not covered by
    /// the segmentation rate limiter: uploads do not trigger segma inference
    /// (the worker pool does, and it is bounded by WorkerCount/MaxAttempts) and
    /// a full commune grid would blow the 10/10min tier.
    /// </summary>
    [HttpPost("{jobId:guid}/chunks/{chunkId:guid}/raster")]
#pragma warning disable S5693 // RequestSizeLimit(50MB) is an intentional, bounded upload cap for imagery tiles
    [RequestSizeLimit(50_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
#pragma warning restore S5693
    public async Task<ActionResult<GenerationJobView>> UploadChunkRaster(
        Guid jobId,
        Guid chunkId,
        [FromForm] JobRasterUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Raster is null || request.Raster.Length == 0)
        {
            return Problem(detail: "raster file is required.", statusCode: 400);
        }

        var contentType = request.Raster.ContentType;
        var extension = Path.GetExtension(request.Raster.FileName);
        if (!AllowedTileContentTypes.Contains(contentType))
        {
            logger.LogWarning("Rejected job raster upload with disallowed content type '{ContentType}'", contentType);
            return Problem(
                detail: "Uploaded raster must be a recognized image type (TIFF, JPEG, PNG or WebP).",
                statusCode: 400);
        }

        if (string.IsNullOrEmpty(extension) || !AllowedTileExtensions.Contains(extension))
        {
            logger.LogWarning("Rejected job raster upload with no/unsupported file extension '{FileName}'", request.Raster.FileName);
            return Problem(
                detail: "Uploaded raster must have a recognized image file extension (.tif, .tiff, .jpg, .jpeg, .png or .webp).",
                statusCode: 400);
        }

        try
        {
            await using var buffer = new MemoryStream();
            await using var upload = request.Raster.OpenReadStream();
            await upload.CopyToAsync(buffer, cancellationToken);

            var view = await generationJobService.UploadChunkRasterAsync(
                jobId, chunkId, CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
                buffer.ToArray(), request.Raster.FileName, contentType, cancellationToken);
            return Ok(view);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return Problem(detail: "The chunk or job was not found.", statusCode: 404);
        }
        catch (InvalidOperationException ex)
        {
            return Problem(detail: ex.Message, statusCode: 409);
        }
    }

    /// <summary>
    /// Cancels a job: chunks still open become <c>cancelled</c>, drafts already
    /// segmented stay in the review queue (never promoted), and the acceptance
    /// pass is skipped. No-op on terminal jobs.
    /// </summary>
    [HttpPost("{jobId:guid}/cancel")]
    public async Task<ActionResult<GenerationJobView>> Cancel(
        Guid jobId,
        CancellationToken cancellationToken)
    {
        try
        {
            var view = await generationJobService.CancelAsync(
                jobId, CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId, cancellationToken);
            return Ok(view);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException)
        {
            return Problem(detail: "The generation job was not found.", statusCode: 404);
        }
    }
}
