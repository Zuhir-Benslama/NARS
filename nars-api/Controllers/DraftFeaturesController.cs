using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("api/draft-features")]
[Authorize]
public sealed class DraftFeaturesController(
    IDraftFeaturesService draftFeaturesService,
    ILogger<DraftFeaturesController> logger,
    IWebHostEnvironment webHost) : NarsControllerBase(webHost)
{
    // Accepted image mime types for geo-referenced tiles submitted to segmentation.
    private static readonly HashSet<string> AllowedTileContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/tiff", "image/tif", "image/jpeg", "image/jpg", "image/png", "image/webp",
    };

    // File extensions mapped from the accepted mime types above.
    private static readonly HashSet<string> AllowedTileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".tif", ".tiff", ".jpeg", ".jpg", ".png", ".webp",
    };

    /// <summary>
    /// Submits an imagery tile for the given commune, runs building
    /// segmentation, and stores the results as pending draft features.
    /// The caller must have access to the commune. Does not touch production
    /// feature tables.
    /// </summary>
    [HttpPost("segment")]
    // 50MB cap: a 1024x1024 georeferenced tile is typically a few MB, so this
    // allows headroom without disabling limits. Above Sonar's 8MB default
    // threshold by design (see FeatureDefaults:MultipartBodyLengthLimit). The
    // uploaded file's content type and extension are validated below so the
    // large bound is not an arbitrary-upload vector.
#pragma warning disable S5693 // RequestSizeLimit(50MB) is an intentional, bounded upload cap for imagery tiles
    [RequestSizeLimit(50_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
#pragma warning restore S5693
    public async Task<ActionResult<SegmentSummaryResponse>> SegmentTile(
        [FromForm] SegmentTileRequest request,
        CancellationToken cancellationToken)
    {
        if (request.CommuneId is null)
        {
            return Problem(detail: "communeId is required.", statusCode: 400);
        }

        if (request.Tile.Length == 0)
        {
            return Problem(detail: "Uploaded tile is empty.", statusCode: 400);
        }

        var contentType = request.Tile.ContentType;
        var extension = Path.GetExtension(request.Tile.FileName);
        if (!AllowedTileContentTypes.Contains(contentType))
        {
            logger.LogWarning("Rejected tile upload with disallowed content type '{ContentType}'", contentType);
            return Problem(
                detail: "Uploaded tile must be a recognized image type (TIFF, JPEG, PNG or WebP).",
                statusCode: 400);
        }

        if (string.IsNullOrEmpty(extension) || !AllowedTileExtensions.Contains(extension))
        {
            logger.LogWarning("Rejected tile upload with no/unsupported file extension '{FileName}'", request.Tile.FileName);
            return Problem(
                detail: "Uploaded tile must have a recognized image file extension (.tif, .tiff, .jpg, .jpeg, .png or .webp).",
                statusCode: 400);
        }

        if (request.MinLon is null || request.MinLat is null || request.MaxLon is null || request.MaxLat is null)
        {
            return Problem(detail: "minLon, minLat, maxLon and maxLat are required.", statusCode: 400);
        }

        try
        {
            await using var stream = request.Tile.OpenReadStream();
            var summary = await draftFeaturesService.SegmentTileAsync(
                CurrentUserRole,
                CurrentCommuneId,
                CurrentDairaId,
                CurrentWilayaId,
                request.CommuneId.Value,
                stream,
                request.Tile.FileName,
                request.Tile.ContentType,
                (request.MinLon.Value, request.MinLat.Value, request.MaxLon.Value, request.MaxLat.Value),
                cancellationToken);
            return Ok(summary);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (KeyNotFoundException ex)
        {
            logger.LogWarning(ex, "Segmentation target commune not found: {Reason}", ex.Message);
            return Problem(detail: "The requested commune was not found.", statusCode: 404);
        }
        catch (SegmentationServiceException ex)
        {
            logger.LogError(ex, "Segmentation service call failed for commune {CommuneId}", request.CommuneId);
            return Problem(detail: "Segmentation service is unavailable.", statusCode: 502);
        }
    }

    /// <summary>
    /// Lists draft features for a commune, for the review queue UI.
    /// The caller must have access to the commune.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<AiDraftFeatureDto>>> ListDrafts(
        [FromQuery] int communeId,
        [FromQuery] string? featureType,
        [FromQuery] string status = "pending",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        (skip, take) = Pagination.Clamp(skip, take);

        try
        {
            var drafts = await draftFeaturesService.ListDraftsAsync(
                CurrentUserRole,
                CurrentCommuneId,
                CurrentDairaId,
                CurrentWilayaId,
                communeId,
                featureType,
                status,
                skip,
                take,
                cancellationToken);
            return Ok(drafts);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>
    /// Accepts a draft: marks it accepted (promotion into the production feature
    /// table is the caller's responsibility — pass GeometryGeoJson/FeatureType
    /// through the same service that handles field-worker-drawn features).
    /// Requires reviewer-level authorization and access to the draft's commune.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = "CanReviewFeatures")]
    public async Task<IActionResult> AcceptDraft(Guid id, CancellationToken cancellationToken)
        => await ReviewDraftAsync(id, accept: true, cancellationToken);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "CanReviewFeatures")]
    public async Task<IActionResult> RejectDraft(Guid id, CancellationToken cancellationToken)
        => await ReviewDraftAsync(id, accept: false, cancellationToken);

    private async Task<IActionResult> ReviewDraftAsync(Guid id, bool accept, CancellationToken ct)
    {
        var result = accept
            ? await draftFeaturesService.AcceptDraftAsync(
                CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
                RequiredCurrentUserId, id, ct)
            : await draftFeaturesService.RejectDraftAsync(
                CurrentUserRole, CurrentCommuneId, CurrentDairaId, CurrentWilayaId,
                RequiredCurrentUserId, id, ct);

        return result.Status switch
        {
            DraftReviewStatus.Success => NoContent(),
            DraftReviewStatus.NotFound => NotFound(),
            DraftReviewStatus.AlreadyReviewed => Problem(detail: $"Draft {id} is not pending.", statusCode: 409),
            _ => Forbid(),
        };
    }
}
