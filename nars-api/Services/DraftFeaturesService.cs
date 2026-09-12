using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Encapsulates the AI draft-feature workflow: submitting an imagery tile for
/// segmentation, listing the review queue, editing drafts, and accepting or
/// rejecting them. Accepting a draft materializes the production feature (a
/// road row for road drafts) so the AI suggestion becomes a normal, editable
/// feature — it does not just flip a status flag. Every operation verifies
/// that the caller's role + geographic scope covers the commune the drafts
/// belong to, so no authenticated user can read or modify another commune's
/// queue.
/// </summary>
public interface IDraftFeaturesService
{
    /// <summary>
    /// Runs segmentation on the uploaded tile and persists the results as
    /// pending draft features for the commune. `featureType` selects the
    /// model/endpoint ("building" or "road"); each request targets exactly
    /// one feature type. Returns counts for both feature types (one will be
    /// zero). Throws when the caller has no access to the commune or the
    /// commune does not exist.
    /// </summary>
    Task<SegmentSummaryResponse> SegmentTileAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, string featureType, Stream tileStream, string fileName, string contentType,
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

    /// <summary>
    /// Replaces a pending draft's geometry. The new geometry must keep the same
    /// GeoJSON kind as the draft's feature type (LineString for roads, Polygon
    /// for buildings). Returns Forbidden when the caller lacks commune scope,
    /// AlreadyReviewed when the draft is no longer pending.
    /// </summary>
    Task<DraftReviewResult> UpdateDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, string geometryGeoJson, CancellationToken ct);

    /// <summary>Deletes a pending draft if the caller may review it.</summary>
    Task<DraftReviewResult> DeleteDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct);

    /// <summary>
    /// Marks a draft accepted if the caller may review it. Road drafts are
    /// materialized into the production roads table (subject to the road
    /// rules); building drafts are materialized as auto-numbered house
    /// entrances on the nearest road in the commune. Returns RulesNotMet when
    /// a draft violates the configured rules.
    /// </summary>
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
    RulesNotMet,
    InvalidGeometry,
}

public class DraftFeaturesService(
    IDbContextFactory<AppDbContext> dbFactory,
    ISegmentationClient segmentationClient,
    ICommuneScopeService communeScope,
    IDateTimeProvider timeProvider,
    IOptions<RoadRulesOptions> roadRules,
    IOptions<BuildingRulesOptions> buildingRules) : IDraftFeaturesService
{
    private static readonly string HouseEntrancesTable =
        FeatureTypeRegistry.GetDescriptor(FeatureTypes.HouseEntrance)?.TableName ?? "house_entrances";

    public async Task<SegmentSummaryResponse> SegmentTileAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, string featureType, Stream tileStream, string fileName, string contentType,
        (double MinLon, double MinLat, double MaxLon, double MaxLat) bbox,
        CancellationToken ct)
    {
        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("You do not have access to this commune.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        _ = await db.Communes.FindAsync([communeId], ct) ?? throw new KeyNotFoundException($"Commune {communeId} not found");
        SegmentationResult result;
        result = await segmentationClient.SegmentTileAsync(
            featureType, tileStream, fileName, contentType, bbox, ct);

        var now = timeProvider.UtcNow;
        var draftEntities = new List<AiDraftFeature>();

        var features = featureType.Equals(AiDraftFeature.TypeRoad, StringComparison.OrdinalIgnoreCase)
            ? result.Roads
            : result.Buildings;

        foreach (var feature in features)
        {
            draftEntities.Add(AiDraftFeature.Create(
                featureType: feature.FeatureType,
                geometryGeoJson: feature.GeometryGeoJson,
                confidence: feature.Confidence,
                communeId: communeId,
                sourceTileRef: fileName,
                createdAt: now));
        }

        db.AiDraftFeatures.AddRange(draftEntities);
        await db.SaveChangesAsync(ct);

        return new SegmentSummaryResponse
        {
            BuildingCount = result.Buildings.Count,
            RoadCount = result.Roads.Count,
            DraftIds = [.. draftEntities.Select(d => d.Id)],
        };
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

        await using var db = await dbFactory.CreateDbContextAsync(ct);

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

    public async Task<DraftReviewResult> UpdateDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, string geometryGeoJson, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var draft = await db.AiDraftFeatures.FirstOrDefaultAsync(f => f.Id == draftId, ct);
        if (draft is null)
        {
            return new DraftReviewResult(DraftReviewStatus.NotFound);
        }

        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, draft.CommuneId, ct))
        {
            return new DraftReviewResult(DraftReviewStatus.Forbidden);
        }

        if (draft.Status != AiDraftFeature.StatusPending)
        {
            return new DraftReviewResult(DraftReviewStatus.AlreadyReviewed);
        }

        // An edit must keep the draft's geometry kind: a road draft is a
        // LineString, a building draft a Polygon. Anything else would silently
        // change what the acceptance step materializes.
        var expectedKind = draft.FeatureType == AiDraftFeature.TypeRoad ? "LineString" : "Polygon";
        if (!DraftGeometry.IsGeometryKind(geometryGeoJson, expectedKind))
        {
            return new DraftReviewResult(DraftReviewStatus.InvalidGeometry);
        }

        var entry = db.Entry(draft);
        entry.Property(f => f.GeometryGeoJson).CurrentValue = geometryGeoJson;
        await db.SaveChangesAsync(ct);

        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    public async Task<DraftReviewResult> DeleteDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var draft = await db.AiDraftFeatures.FirstOrDefaultAsync(f => f.Id == draftId, ct);
        if (draft is null)
        {
            return new DraftReviewResult(DraftReviewStatus.NotFound);
        }

        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, draft.CommuneId, ct))
        {
            return new DraftReviewResult(DraftReviewStatus.Forbidden);
        }

        if (draft.Status != AiDraftFeature.StatusPending)
        {
            return new DraftReviewResult(DraftReviewStatus.AlreadyReviewed);
        }

        db.AiDraftFeatures.Remove(draft);
        await db.SaveChangesAsync(ct);

        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    public Task<DraftReviewResult> AcceptDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct)
        => AcceptDraftCoreAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            userId, draftId, ct);

    public Task<DraftReviewResult> RejectDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct)
        => ReviewDraftAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId,
            userId, draftId, accept: false, ct);

    private async Task<DraftReviewResult> AcceptDraftCoreAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Read-only: the status transition below goes through a conditional
        // ExecuteUpdateAsync, so the draft is loaded without change tracking.
        var draft = await db.AiDraftFeatures.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == draftId, ct);
        if (draft is null)
        {
            return new DraftReviewResult(DraftReviewStatus.NotFound);
        }

        if (!await CanAccessCommuneAsync(callerRole, callerCommuneId, callerDairaId, callerWilayaId, draft.CommuneId, ct))
        {
            return new DraftReviewResult(DraftReviewStatus.Forbidden);
        }

        if (draft.Status != AiDraftFeature.StatusPending)
        {
            return new DraftReviewResult(DraftReviewStatus.AlreadyReviewed);
        }

        if (draft.FeatureType == AiDraftFeature.TypeRoad)
        {
            return await AcceptRoadDraftAsync(db, draft, userId, ct);
        }

        // Building drafts are materialized as auto-numbered house entrances on
        // the nearest road in the commune (Phase 2).
        return await AcceptBuildingDraftAsync(db, draft, userId, ct);
    }

    /// <summary>
    /// Materializes an accepted road draft into the production roads table.
    /// The road rules (minimum geodesic length, minimum confidence — mirrored
    /// from segma's NARS_SEGMA_ROAD_* env) gate the promotion, so a stub spur
    /// or a weak detection that slipped past postprocessing is still blocked
    /// here. The status transition reserves the draft first (one concurrent
    /// reviewer wins); the loser's not-yet-saved road row is detached, never
    /// committed.
    /// </summary>
    private async Task<DraftReviewResult> AcceptRoadDraftAsync(
        AppDbContext db, AiDraftFeature draft, Guid userId, CancellationToken ct)
    {
        if (!DraftGeometry.TryGetLineLengthM(draft.GeometryGeoJson, out var lengthM)
            || lengthM < roadRules.Value.MinRoadLengthM)
        {
            return new DraftReviewResult(DraftReviewStatus.RulesNotMet);
        }

        if (draft.Confidence < roadRules.Value.MinConfidence)
        {
            return new DraftReviewResult(DraftReviewStatus.RulesNotMet);
        }

        var dataJson = DraftGeometry.ToRoadData(draft.GeometryGeoJson).ToJsonString();
        var entity = FeatureTypeRegistry.CreateEntity(
            FeatureTypes.Road,
            Guid.CreateVersion7(),
            userId,
            FeatureTypes.RoadLayers.Street,
            label: string.Empty,
            dataJson)
            ?? throw new InvalidOperationException("FeatureTypeRegistry has no Road descriptor");
        var entry = FeatureTypeRegistry.AddToDbContext(db, entity)
            ?? throw new InvalidOperationException("FeatureTypeRegistry has no Road descriptor");

        var reviewedAt = timeProvider.UtcNow;
        var affected = await TryReviewDraftAsync(db, draft.Id, AiDraftFeature.StatusAccepted, userId, reviewedAt, ct);
        if (affected == 0)
        {
            // Lost the review race: never let the orphaned road row commit.
            entry.State = EntityState.Detached;
            return ResolveConflict(db, draft.Id);
        }

        await db.SaveChangesAsync(ct);
        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    private sealed record RoadCandidate(Guid RoadId, Guid OwnerUserId, string Label, IReadOnlyList<(double Lat, double Lng)> Coords);

    private sealed record RoadReference(
        RoadCandidate Road, int SegmentIndex, double DistanceM, (double Lat, double Lng) EntrancePoint, string Side);

    /// <summary>
    /// Materializes an accepted building draft as a house entrance on the
    /// nearest road in the commune. The footprint's front vertex facing that
    /// road becomes the entrance point; the side (left/right) and the house
    /// number (odd left / even right, next free in the parity series) come from
    /// the same conventions NumberEntrancesService applies. The building rules
    /// (minimum confidence) gate the promotion.
    ///
    /// The whole promotion runs inside one transaction that first locks the
    /// reference road, so two concurrent accepts of different buildings on the
    /// same road can never pick the same number. Virtual so the InMemory unit
    /// suite can substitute a tracked equivalent (the InMemory provider does not
    /// implement BeginTransactionAsync).
    /// </summary>
    protected virtual async Task<DraftReviewResult> AcceptBuildingDraftAsync(
        AppDbContext db, AiDraftFeature draft, Guid userId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var result = await AcceptBuildingDraftCoreAsync(db, draft, userId, ct);
        if (result.Status == DraftReviewStatus.Success)
        {
            // Commit on success; on any failure, disposing the transaction
            // rolls back the number reservation and the pending entrance insert.
            await tx.CommitAsync(ct);
        }

        return result;
    }

    /// <summary>
    /// Shared build-accept logic, free of any Postgres-specific primitive so
    /// both the transactional production path and the InMemory test seam run
    /// the exact same decisions. The draft-reservation conditional update
    /// happens first (exactly one concurrent reviewer wins), the entrance insert
    /// second — the loser returns before SaveChanges, so nothing is written.
    /// </summary>
    protected async Task<DraftReviewResult> AcceptBuildingDraftCoreAsync(
        AppDbContext db, AiDraftFeature draft, Guid userId, CancellationToken ct)
    {
        if (draft.Confidence < buildingRules.Value.MinConfidence)
        {
            return new DraftReviewResult(DraftReviewStatus.RulesNotMet);
        }

        if (!DraftGeometry.TryGetPolygonRing(draft.GeometryGeoJson, out var ring))
        {
            return new DraftReviewResult(DraftReviewStatus.InvalidGeometry);
        }

        var roads = await LoadCommuneRoadsAsync(db, draft.CommuneId, ct);
        var reference = FindBestReference(roads, ring);
        if (reference is null)
        {
            // No road in the commune to anchor the entrance to: keep the draft
            // pending so it can be accepted once a road is materialized.
            return new DraftReviewResult(DraftReviewStatus.RulesNotMet);
        }

        var number = await NextEntranceNumberAsync(db, reference.Road.OwnerUserId, reference.Road.RoadId, reference.Side, ct);
        if (number is null)
        {
            return new DraftReviewResult(DraftReviewStatus.RulesNotMet);
        }

        var label = number.Value.ToString(CultureInfo.InvariantCulture);
        var dataJson = DraftGeometry.ToHouseEntranceData(
            reference.Road.RoadId, reference.Road.Label, reference.Side, number,
            reference.EntrancePoint.Lat, reference.EntrancePoint.Lng, draft.GeometryGeoJson).ToJsonString();

        var entranceId = Guid.CreateVersion7();
        var entity = FeatureTypeRegistry.CreateEntity(
            FeatureTypes.HouseEntrance,
            entranceId,
            reference.Road.OwnerUserId,
            FeatureTypes.HouseEntranceLayers.Main,
            label,
            dataJson)
            ?? throw new InvalidOperationException("FeatureTypeRegistry has no HouseEntrance descriptor");
        ((HouseEntrance)entity).RoadId = reference.Road.RoadId;
        var entry = FeatureTypeRegistry.AddToDbContext(db, entity)
            ?? throw new InvalidOperationException("FeatureTypeRegistry has no HouseEntrance descriptor");
        db.FeatureRegistry.Add(new FeatureRegistry { Id = entranceId, FeatureType = FeatureTypes.HouseEntrance });

        var reviewedAt = timeProvider.UtcNow;
        var affected = await TryReviewDraftAsync(db, draft.Id, AiDraftFeature.StatusAccepted, userId, reviewedAt, ct);
        if (affected == 0)
        {
            // Lost the review race: never let the orphaned entrance commit.
            entry.State = EntityState.Detached;
            return ResolveConflict(db, draft.Id);
        }

        await db.SaveChangesAsync(ct);
        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    /// <summary>
    /// Assigns the next free entrance number on the side's parity series (odd
    /// left / even right) under a row lock on the reference road — the same
    /// serialization anchor NumberEntrancesService uses. Raw SQL because the
    /// lock must hold until the surrounding transaction commits. Returns null
    /// when the road is gone (or not owned) or the parity series is exhausted.
    /// Virtual so the InMemory unit suite can substitute an EF query.
    /// </summary>
    protected virtual async Task<int?> NextEntranceNumberAsync(
        AppDbContext db, Guid roadOwnerId, Guid roadId, string side, CancellationToken ct)
    {
        var dbTx = db.Database.CurrentTransaction?.GetDbTransaction()
            ?? throw new InvalidOperationException("House-entrance numbering requires an active database transaction.");

        var conn = db.Database.GetDbConnection();
        await using (var lockCmd = conn.CreateCommand())
        {
            lockCmd.Transaction = dbTx;
            lockCmd.CommandText = "SELECT 1 FROM roads WHERE id = @rid AND user_id = @uid FOR UPDATE";
            SqlFragments.AddParam(lockCmd, "@rid", roadId);
            SqlFragments.AddParam(lockCmd, "@uid", roadOwnerId);
            if (await lockCmd.ExecuteScalarAsync(ct) is null)
            {
                return null;
            }
        }

        var usedNumbers = new HashSet<int>();
        await using (var rowsCmd = conn.CreateCommand())
        {
            rowsCmd.Transaction = dbTx;
#pragma warning disable S2077 // Table name is allowlist-validated; values are parameterized
            rowsCmd.CommandText =
                $"SELECT data FROM {HouseEntrancesTable} " +
                "WHERE user_id = @uid AND road_id = @rid AND layer = @layer";
#pragma warning restore S2077
            SqlFragments.AddParam(rowsCmd, "@uid", roadOwnerId);
            SqlFragments.AddParam(rowsCmd, "@rid", roadId);
            SqlFragments.AddParam(rowsCmd, "@layer", FeatureTypes.HouseEntranceLayers.Main);

            await using var reader = await rowsCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (TryReadEntranceNumber(reader.GetString(0), side, out var num))
                {
                    usedNumbers.Add(num);
                }
            }
        }

        var suggested = GeometryHelper.SuggestEntranceNumber(side, usedNumbers);
        return suggested < 0 ? null : suggested;
    }

    /// <summary>
    /// Reads the side + entranceNumber out of an entrance's data JSONB. Used by
    /// the numbering seam to build the used-number set. Internal so the testable
    /// subclass can share the exact parse.
    /// </summary>
    protected internal static bool TryReadEntranceNumber(string data, string side, out int number)
    {
        number = 0;
        try
        {
            var node = JsonNode.Parse(data);
            if (node?["side"]?.GetValue<string>() != side)
            {
                return false;
            }

            if (node?["entranceNumber"] is { } numNode)
            {
                number = numNode.GetValue<int>();
                return true;
            }
        }
        catch
        {
            // A malformed row (bad JSON or non-integer number) simply doesn't
            // contribute to the used set; numbering still proceeds safely.
        }

        return false;
    }

    private static async Task<List<RoadCandidate>> LoadCommuneRoadsAsync(AppDbContext db, int communeId, CancellationToken ct)
    {
        // Roads have no commune column; commune scope comes from their owner
        // user. This is the same join EntranceService uses for road ownership.
        var rows = await (
            from r in db.Roads
            join u in db.Users on r.UserId equals u.Id
            where u.CommuneId == communeId
            select new { r.Id, r.UserId, r.Label, r.Data }
        ).ToListAsync(ct);

        var candidates = new List<RoadCandidate>(rows.Count);
        foreach (var row in rows)
        {
            var coords = TryParseRoadCoordinates(row.Data);
            if (coords is { Count: >= 2 })
            {
                candidates.Add(new RoadCandidate(row.Id, row.UserId, row.Label, coords));
            }
        }

        return candidates;
    }

    private static IReadOnlyList<(double Lat, double Lng)>? TryParseRoadCoordinates(string data)
    {
        try
        {
            var node = JsonNode.Parse(data);
            var coordsNode = node?["coordinates"];
            if (coordsNode is null)
            {
                return null;
            }

            return GeometryHelper.ParseRoadCoordinates(coordsNode);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Picks the nearest road segment to the building footprint among all roads
    /// in the commune, using the centroid for the side/distance decisions (the
    /// small footprints segma produces make the mean vertex a stable anchor).
    /// Returns null when no road is within <see cref="DraftGeometry.MaxReferenceRoadDistanceM"/>.
    /// </summary>
    private static RoadReference? FindBestReference(
        IReadOnlyList<RoadCandidate> candidates, IReadOnlyList<(double Lon, double Lat)> ring)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        var (centroidLon, centroidLat) = DraftGeometry.PolygonCentroid(ring);
        RoadReference? nearest = null;

        foreach (var road in candidates)
        {
            if (road.Coords.Count < 2)
            {
                continue;
            }

            var segmentIndex = GeometryHelper.FindNearestSegmentIndex(centroidLat, centroidLon, road.Coords);
            var start = road.Coords[segmentIndex];
            var end = road.Coords[segmentIndex + 1];
            var midLat = (start.Lat + end.Lat) / 2.0;
            var midLng = (start.Lng + end.Lng) / 2.0;

            var distanceM = DraftGeometry.HaversineM(centroidLon, centroidLat, midLng, midLat);
            if (nearest is not null && distanceM >= nearest.DistanceM)
            {
                continue;
            }

            var entrancePoint = DraftGeometry.FindNearestRingVertex(midLng, midLat, ring);
            var side = GeometryHelper.DetermineSide(
                centroidLat, centroidLon, start.Lat, start.Lng, end.Lat, end.Lng);
            nearest = new RoadReference(road, segmentIndex, distanceM, entrancePoint, side);
        }

        return nearest is not null && nearest.DistanceM <= DraftGeometry.MaxReferenceRoadDistanceM ? nearest : null;
    }

    private async Task<DraftReviewResult> ReviewDraftAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        Guid userId, Guid draftId, bool accept, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Read-only: the status transition below goes through a conditional
        // ExecuteUpdateAsync, so the draft is loaded without change tracking.
        var draft = await db.AiDraftFeatures.AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == draftId, ct);
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
        var affected = await TryReviewDraftAsync(db, draftId, newStatus, userId, reviewedAt, ct);

        if (affected == 0)
        {
            return ResolveConflict(db, draftId);
        }

        return new DraftReviewResult(DraftReviewStatus.Success);
    }

    private static DraftReviewResult ResolveConflict(AppDbContext db, Guid draftId)
    {
        // The conditional update matched nothing: either the draft is gone or
        // another reviewer already transitioned it. Distinguish the two so the
        // loser reports the truthful outcome instead of a guess.
        var stillExists = db.AiDraftFeatures.AsNoTracking().Any(f => f.Id == draftId);
        return stillExists
            ? new DraftReviewResult(DraftReviewStatus.AlreadyReviewed)
            : new DraftReviewResult(DraftReviewStatus.NotFound);
    }

    /// <summary>
    /// Applies the review status transition as a single conditional UPDATE ... WHERE
    /// Status = 'pending', returning the number of affected rows. Kept virtual so
    /// tests can substitute an equivalent tracked update for the InMemory provider,
    /// which does not implement ExecuteUpdateAsync.
    /// </summary>
    protected virtual async Task<int> TryReviewDraftAsync(
        AppDbContext db, Guid draftId, string newStatus, Guid reviewedBy, DateTimeOffset reviewedAt, CancellationToken ct) => await db.AiDraftFeatures
            .Where(f => f.Id == draftId && f.Status == AiDraftFeature.StatusPending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.Status, newStatus)
                .SetProperty(f => f.ReviewedBy, reviewedBy)
                .SetProperty(f => f.ReviewedAt, reviewedAt), ct);

    private Task<bool> CanAccessCommuneAsync(
        string callerRole, int? callerCommuneId, int? callerDairaId, int? callerWilayaId,
        int communeId, CancellationToken ct)
        => communeScope.CanAccessCommuneAsync(
            callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct);
}
