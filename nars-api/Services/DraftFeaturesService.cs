using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Encapsulates the AI draft-feature workflow: submitting an imagery tile for
/// segmentation, listing the review queue, and accepting/rejecting drafts.
/// Every operation verifies that the caller's role + geographic scope covers
/// the commune the drafts belong to, so no authenticated user can read or
/// modify another commune's queue.
/// </summary>
public interface IDraftFeaturesService
{
    /// <summary>
    /// Runs road+building segmentation on the uploaded tile and persists the
    /// results as pending draft features for the commune. Returns an empty
    /// list of ids when no features were detected. Throws when the caller has
    /// no access to the commune or the commune does not exist.
    /// </summary>
    Task<SegmentSummaryResponse> SegmentTileAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, Stream tileStream, string fileName, string contentType,
        (double MinLon, double MinLat, double MaxLon, double MaxLat) bbox,
        CancellationToken ct);

    /// <summary>
    /// Lists pending draft features for a commune. Throws when the caller has
    /// no access to the commune.
    /// </summary>
    Task<List<AiDraftFeatureDto>> ListDraftsAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, string? featureType, string status,
        CancellationToken ct);

    /// <summary>Marks a draft accepted if the caller may review it.</summary>
    Task<DraftReviewResult> AcceptDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct);

    /// <summary>Marks a draft rejected if the caller may review it.</summary>
    Task<DraftReviewResult> RejectDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct);
}

public sealed record DraftReviewResult(DraftReviewStatus Status);

public enum DraftReviewStatus
{
    Success,
    NotFound,
    AlreadyReviewed,
    Forbidden,
}

public sealed class DraftFeaturesService(
    AppDbContext db,
    ISegmentationClient segmentationClient,
    ICommuneScopeService communeScope) : IDraftFeaturesService
{
    private readonly AppDbContext _db = db;
    private readonly ISegmentationClient _segmentationClient = segmentationClient;
    private readonly ICommuneScopeService _communeScope = communeScope;

    public async Task<SegmentSummaryResponse> SegmentTileAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, Stream tileStream, string fileName, string contentType,
        (double MinLon, double MinLat, double MaxLon, double MaxLat) bbox,
        CancellationToken ct)
    {
        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this commune.");
        }

        var commune = await _db.Communes.FindAsync([communeId], ct);
        if (commune is null)
        {
            throw new KeyNotFoundException($"Commune {communeId} not found");
        }

        SegmentationResult result;
        await using var stream = tileStream;
        result = await _segmentationClient.SegmentTileAsync(
            stream, fileName, contentType, bbox, ct);

        var now = DateTimeOffset.UtcNow;
        var draftEntities = new List<AiDraftFeature>();

        foreach (var road in result.Roads)
        {
            draftEntities.Add(AiDraftFeature.Create(
                featureType: "road",
                geometryGeoJson: road.GeometryGeoJson,
                confidence: road.Confidence,
                communeId: communeId,
                sourceTileRef: fileName,
                createdAt: now));
        }

        foreach (var building in result.Buildings)
        {
            draftEntities.Add(AiDraftFeature.Create(
                featureType: "building",
                geometryGeoJson: building.GeometryGeoJson,
                confidence: building.Confidence,
                communeId: communeId,
                sourceTileRef: fileName,
                createdAt: now));
        }

        _db.AiDraftFeatures.AddRange(draftEntities);
        await _db.SaveChangesAsync(ct);

        return new SegmentSummaryResponse(
            RoadCount: result.Roads.Count,
            BuildingCount: result.Buildings.Count,
            DraftIds: draftEntities.Select(d => d.Id).ToList());
    }

    public async Task<List<AiDraftFeatureDto>> ListDraftsAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, string? featureType, string status,
        CancellationToken ct)
    {
        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this commune.");
        }

        var query = _db.AiDraftFeatures
            .Where(f => f.CommuneId == communeId && f.Status == status);

        if (!string.IsNullOrEmpty(featureType))
        {
            query = query.Where(f => f.FeatureType == featureType);
        }

        return await query
            .OrderByDescending(f => f.Confidence)
            .Select(f => new AiDraftFeatureDto(
                f.Id, f.FeatureType, f.GeometryGeoJson, f.Confidence, f.Status, f.CreatedAt))
            .ToListAsync(ct);
    }

    public Task<DraftReviewResult> AcceptDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct)
        => ReviewDraftAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            userId, draftId, accept: true, ct);

    public Task<DraftReviewResult> RejectDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct)
        => ReviewDraftAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            userId, draftId, accept: false, ct);

    private async Task<DraftReviewResult> ReviewDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, bool accept, CancellationToken ct)
    {
        var draft = await _db.AiDraftFeatures.FindAsync([draftId], ct);
        if (draft is null)
        {
            return new DraftReviewResult(DraftReviewStatus.NotFound);
        }

        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, draft.CommuneId, ct))
        {
            return new DraftReviewResult(DraftReviewStatus.Forbidden);
        }

        if (draft.Status != "pending")
        {
            return new DraftReviewResult(DraftReviewStatus.AlreadyReviewed);
        }

        if (accept)
        {
            draft.MarkAccepted(reviewedBy: userId, reviewedAt: DateTimeOffset.UtcNow);
        }
        else
        {
            draft.MarkRejected(reviewedBy: userId, reviewedAt: DateTimeOffset.UtcNow);
        }

        await _db.SaveChangesAsync(ct);
        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    private Task<bool> CanAccessCommuneAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, CancellationToken ct)
        => _communeScope.CanAccessCommuneAsync(
            callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct);
}
