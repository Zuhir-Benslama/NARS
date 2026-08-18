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
    /// Runs building segmentation on the uploaded tile and persists the
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
    /// Lists draft features for a commune. Throws when the caller has
    /// no access to the commune.
    /// </summary>
    Task<PagedResponse<AiDraftFeatureDto>> ListDraftsAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, string? featureType, string status,
        int skip = 0, int take = 100,
        CancellationToken ct = default);

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

public class DraftFeaturesService(
    AppDbContext db,
    ISegmentationClient segmentationClient,
    ICommuneScopeService communeScope,
    IDateTimeProvider timeProvider) : IDraftFeaturesService
{

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

        var commune = await db.Communes.FindAsync([communeId], ct);
        if (commune is null)
        {
            throw new KeyNotFoundException($"Commune {communeId} not found");
        }

        SegmentationResult result;
        result = await segmentationClient.SegmentTileAsync(
            tileStream, fileName, contentType, bbox, ct);

        var now = timeProvider.UtcNow;
        var draftEntities = new List<AiDraftFeature>();

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

        db.AiDraftFeatures.AddRange(draftEntities);
        await db.SaveChangesAsync(ct);

        return new SegmentSummaryResponse(
            BuildingCount: result.Buildings.Count,
            DraftIds: draftEntities.Select(d => d.Id).ToList());
    }

    public async Task<PagedResponse<AiDraftFeatureDto>> ListDraftsAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, string? featureType, string status,
        int skip = 0, int take = 100,
        CancellationToken ct = default)
    {
        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this commune.");
        }

        var query = db.AiDraftFeatures
            .Where(f => f.CommuneId == communeId && f.Status == status);

        if (!string.IsNullOrEmpty(featureType))
        {
            query = query.Where(f => f.FeatureType == featureType);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.Confidence)
            .Skip(skip)
            .Take(take)
            .Select(f => new AiDraftFeatureDto(
                f.Id, f.FeatureType, f.GeometryGeoJson, f.Confidence, f.Status, f.CreatedAt))
            .ToListAsync(ct);

        return new PagedResponse<AiDraftFeatureDto>(items, total, skip, take);
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
        var draft = await db.AiDraftFeatures.FindAsync([draftId], ct);
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

        // Atomic conditional update closes the TOCTOU window between the status
        // read above and the write: only one concurrent reviewer can transition
        // a pending draft, and the losing reviewer sees AlreadyReviewed instead
        // of silently overwriting the winner's decision.
        var newStatus = accept ? AiDraftFeature.StatusAccepted : AiDraftFeature.StatusRejected;
        var reviewedAt = timeProvider.UtcNow;
        var affected = await TryReviewDraftAsync(draftId, newStatus, userId, reviewedAt, ct);

        if (affected == 0)
        {
            var stillExists = await db.AiDraftFeatures.AsNoTracking()
                .AnyAsync(f => f.Id == draftId, ct);
            return stillExists
                ? new DraftReviewResult(DraftReviewStatus.AlreadyReviewed)
                : new DraftReviewResult(DraftReviewStatus.NotFound);
        }

        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    /// <summary>
    /// Applies the review status transition as a single conditional UPDATE ... WHERE
    /// Status = 'pending', returning the number of affected rows. Kept virtual so
    /// tests can substitute an equivalent tracked update for the InMemory provider,
    /// which does not implement ExecuteUpdateAsync.
    /// </summary>
    protected virtual async Task<int> TryReviewDraftAsync(
        Guid draftId, string newStatus, Guid reviewedBy, DateTimeOffset reviewedAt, CancellationToken ct)
    {
        return await db.AiDraftFeatures
            .Where(f => f.Id == draftId && f.Status == AiDraftFeature.StatusPending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.Status, newStatus)
                .SetProperty(f => f.ReviewedBy, reviewedBy)
                .SetProperty(f => f.ReviewedAt, reviewedAt), ct);
    }

    private Task<bool> CanAccessCommuneAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, CancellationToken ct)
        => communeScope.CanAccessCommuneAsync(
            callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct);
}
