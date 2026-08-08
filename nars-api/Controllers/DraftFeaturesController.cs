using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;

namespace NarsApi.Controllers;

[ApiController]
[Route("api/draft-features")]
[Authorize]
public sealed class DraftFeaturesController : NarsControllerBase
{
    private readonly ISegmentationClient _segmentationClient;
    private readonly AppDbContext _db;
    private readonly ILogger<DraftFeaturesController> _logger;

    public DraftFeaturesController(
        ISegmentationClient segmentationClient,
        AppDbContext db,
        ILogger<DraftFeaturesController> logger,
        IWebHostEnvironment webHost) : base(webHost)
    {
        _segmentationClient = segmentationClient;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Submits an imagery tile for the given commune, runs road+building
    /// segmentation, and stores the results as pending draft features.
    /// Does not touch production feature tables.
    /// </summary>
    [HttpPost("segment")]
    // 50MB cap: a 1024x1024 georeferenced tile is typically a few MB, so this
    // allows headroom without disabling limits. Above Sonar's 8MB default
    // threshold by design (see FeatureDefaults:MultipartBodyLengthLimit).
#pragma warning disable S5693 // RequestSizeLimit(50MB) is an intentional, bounded upload cap for imagery tiles
    [RequestSizeLimit(50_000_000)]
    [RequestFormLimits(MultipartBodyLengthLimit = 50_000_000)]
#pragma warning restore S5693
    public async Task<ActionResult<SegmentSummaryResponse>> SegmentTile(
        [FromForm] SegmentTileRequest request,
        CancellationToken cancellationToken)
    {
        var commune = await _db.Communes.FindAsync([request.CommuneId], cancellationToken);
        if (commune is null)
        {
            return NotFound($"Commune {request.CommuneId} not found");
        }

        if (request.Tile.Length == 0)
        {
            return BadRequest("Uploaded tile is empty");
        }

        SegmentationResult result;
        try
        {
            await using var stream = request.Tile.OpenReadStream();
            result = await _segmentationClient.SegmentTileAsync(
                stream,
                request.Tile.FileName,
                request.Tile.ContentType,
                (request.MinLon, request.MinLat, request.MaxLon, request.MaxLat),
                cancellationToken);
        }
        catch (SegmentationServiceException ex)
        {
            _logger.LogError(ex, "Segmentation service call failed for commune {CommuneId}", request.CommuneId);
            return StatusCode(StatusCodes.Status502BadGateway, "Segmentation service is unavailable");
        }

        var now = DateTimeOffset.UtcNow;
        var draftEntities = new List<AiDraftFeature>();

        foreach (var road in result.Roads)
        {
            draftEntities.Add(AiDraftFeature.Create(
                featureType: "road",
                geometryGeoJson: road.GeometryGeoJson,
                confidence: road.Confidence,
                communeId: request.CommuneId,
                sourceTileRef: request.Tile.FileName,
                createdAt: now));
        }

        foreach (var building in result.Buildings)
        {
            draftEntities.Add(AiDraftFeature.Create(
                featureType: "building",
                geometryGeoJson: building.GeometryGeoJson,
                confidence: building.Confidence,
                communeId: request.CommuneId,
                sourceTileRef: request.Tile.FileName,
                createdAt: now));
        }

        _db.AiDraftFeatures.AddRange(draftEntities);
        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new SegmentSummaryResponse(
            RoadCount: result.Roads.Count,
            BuildingCount: result.Buildings.Count,
            DraftIds: draftEntities.Select(d => d.Id).ToList()));
    }

    /// <summary>
    /// Lists pending draft features for a commune, for the review queue UI.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<AiDraftFeatureDto>>> ListDrafts(
        [FromQuery] int communeId,
        [FromQuery] string? featureType,
        [FromQuery] string status = "pending",
        CancellationToken cancellationToken = default)
    {
        var query = _db.AiDraftFeatures
            .Where(f => f.CommuneId == communeId && f.Status == status);

        if (!string.IsNullOrEmpty(featureType))
        {
            query = query.Where(f => f.FeatureType == featureType);
        }

        var drafts = await query
            .OrderByDescending(f => f.Confidence)
            .Select(f => new AiDraftFeatureDto(
                f.Id, f.FeatureType, f.GeometryGeoJson, f.Confidence, f.Status, f.CreatedAt))
            .ToListAsync(cancellationToken);

        return Ok(drafts);
    }

    /// <summary>
    /// Accepts a draft: marks it accepted (promotion into the production feature
    /// table is the caller's responsibility — pass GeometryGeoJson/FeatureType
    /// through the same service that handles field-worker-drawn features).
    /// Requires reviewer-level authorization.
    /// </summary>
    [HttpPost("{id:guid}/accept")]
    [Authorize(Policy = "CanReviewFeatures")]
    public async Task<IActionResult> AcceptDraft(Guid id, CancellationToken cancellationToken)
    {
        var draft = await _db.AiDraftFeatures.FindAsync([id], cancellationToken);
        if (draft is null)
        {
            return NotFound();
        }

        if (draft.Status != "pending")
        {
            return Conflict($"Draft {id} is already {draft.Status}");
        }

        draft.MarkAccepted(reviewedBy: RequiredCurrentUserId, reviewedAt: DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }

    [HttpPost("{id:guid}/reject")]
    [Authorize(Policy = "CanReviewFeatures")]
    public async Task<IActionResult> RejectDraft(Guid id, CancellationToken cancellationToken)
    {
        var draft = await _db.AiDraftFeatures.FindAsync([id], cancellationToken);
        if (draft is null)
        {
            return NotFound();
        }

        if (draft.Status != "pending")
        {
            return Conflict($"Draft {id} is already {draft.Status}");
        }

        draft.MarkRejected(reviewedBy: RequiredCurrentUserId, reviewedAt: DateTimeOffset.UtcNow);
        await _db.SaveChangesAsync(cancellationToken);

        return NoContent();
    }
}

public sealed class SegmentTileRequest
{
    [JsonRequired]
    public int CommuneId { get; set; }

    public IFormFile Tile { get; set; } = null!;

    [JsonRequired]
    public double MinLon { get; set; }

    [JsonRequired]
    public double MinLat { get; set; }

    [JsonRequired]
    public double MaxLon { get; set; }

    [JsonRequired]
    public double MaxLat { get; set; }
}

public sealed record SegmentSummaryResponse(int RoadCount, int BuildingCount, List<Guid> DraftIds);

public sealed record AiDraftFeatureDto(
    Guid Id,
    string FeatureType,
    string GeometryGeoJson,
    double Confidence,
    string Status,
    DateTimeOffset CreatedAt);
