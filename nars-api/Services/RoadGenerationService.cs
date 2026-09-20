using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>A single materialized road created from an AI draft.</summary>
public sealed record GeneratedRoad(Guid DbId, string Layer, string Label, JsonObject Data);

/// <summary>
/// Results of a generation pass. <see cref="Created"/> holds the roads written
/// to the production tables; <see cref="Dropped"/> counts the seeds that were
/// processed but rejected by the cadastre rules (left pending), so the UI can
/// report an honest created/rejected breakdown.
/// </summary>
public sealed record RoadGenerationSummary(IReadOnlyList<GeneratedRoad> Created, int Dropped);

public interface IRoadGenerationService
{
    /// <summary>
    /// Materializes pending AI road drafts for a commune into production road
    /// features, enforcing the roads-phase cadastre rules (urban containment,
    /// turn angle, minimum length/confidence, connectivity to a locally
    /// present road network with endpoint snapping) entirely in pure C#.
    /// Rejected seeds are counted and left pending. The caller must have
    /// access to the commune.
    /// </summary>
    Task<RoadGenerationSummary> GenerateAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        Guid userId,
        int communeId,
        IReadOnlyList<Guid> draftIds,
        CancellationToken ct = default);
}

/// <summary>
/// AI road draft → production road pipeline. Consumes the draft ids returned by
/// the segmentation endpoint and keeps only the drafts that obey the roads
/// phase rules, snapping endpoints onto the commune's local road network. Roads
/// are owned by the calling user (the same convention DraftFeaturesService
/// uses when a road draft is accepted) and start on the default "street" layer.
/// </summary>
public class RoadGenerationService(
    IDbContextFactory<AppDbContext> dbFactory,
    ICommuneScopeService communeScope,
    IDateTimeProvider timeProvider,
    IOptions<ValidationOptions> validationOptions,
    IOptions<RoadRulesOptions> roadRulesOptions) : IRoadGenerationService
{
    public async Task<RoadGenerationSummary> GenerateAsync(
        string callerRole,
        int? callerCommuneId,
        int? callerDairaId,
        int? callerWilayaId,
        Guid userId,
        int communeId,
        IReadOnlyList<Guid> draftIds,
        CancellationToken ct = default)
    {
        if (!await communeScope.CanAccessCommuneAsync(
                callerRole, callerCommuneId, callerDairaId, callerWilayaId, communeId, ct))
        {
            throw new UnauthorizedAccessException("Caller has no access to the commune.");
        }

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        // Drafts are commune-scoped, road-typed and pending; requested ids that
        // don't match (already reviewed, another commune, wrong type) are simply
        // absent rather than counted as dropped.
        var drafts = await db.AiDraftFeatures
            .Where(f => f.CommuneId == communeId
                && f.FeatureType == AiDraftFeature.TypeRoad
                && f.Status == AiDraftFeature.StatusPending
                && draftIds.Contains(f.Id))
            .ToListAsync(ct);
        if (drafts.Count == 0)
        {
            return new RoadGenerationSummary([], 0);
        }

        var areaRings = await LoadUrbanAreaRingsAsync(db, communeId, ct);
        var roadNetwork = await LoadRoadNetworkAsync(db, communeId, ct);

        var validation = validationOptions.Value;
        var rules = roadRulesOptions.Value;

        var now = timeProvider.UtcNow;
        var created = new List<GeneratedRoad>(drafts.Count);
        var acceptedIds = new List<Guid>(drafts.Count);
        var dropped = 0;

        foreach (var draft in drafts)
        {
            if (!DraftGeometry.TryGetLineCoordinates(draft.GeometryGeoJson, out var seedVertices))
            {
                dropped++;
                continue;
            }

            var vertices = seedVertices.ToList();
            if (vertices.Count < 2 || LineLengthM(vertices) < rules.MinRoadLengthM)
            {
                dropped++;
                continue;
            }

            if (draft.Confidence < rules.MinConfidence)
            {
                dropped++;
                continue;
            }

            if (VerticesTurnAngleExceeds(vertices, validation.RoadTurnAngleDegrees))
            {
                dropped++;
                continue;
            }

            // Every vertex must lie inside (or within the tolerance of) an urban
            // area — central_urban or secondary_urban polygons only. A road
            // poking out of the urban envelope is rejected outright.
            if (!IsWithinUrbanAreasM(vertices, areaRings, rules.InsideToleranceMeters))
            {
                dropped++;
                continue;
            }

            // Connectivity: with no roads yet, the first generated roads are
            // exempt (the commune starts its network). The same exemption holds
            // when the commune's existing roads are all far away from this
            // corridor — a legacy/demo road (or a mapped district kilometres
            // away) must not block generation here, so the check is scoped to
            // roads within RoadNetworkSearchMeters of the draft. Whenever such
            // a local network exists, both endpoints must lie within the
            // connectivity distance of one of its roads, and are snapped onto
            // it (snapping is restricted to that local set as well).
            var localNetwork = RoadsNearDraft(vertices, roadNetwork, rules.RoadNetworkSearchMeters);
            if (localNetwork.Count > 0 && !ConnectEndpoints(vertices, localNetwork, validation.RoadConnectivityMeters))
            {
                dropped++;
                continue;
            }

            // Snapping runs after the containment/angle checks above, so re-verify
            // the mutated line (the network may sit just outside an area edge, and
            // a snap can introduce an acute turn at the junction).
            if (!IsWithinUrbanAreasM(vertices, areaRings, rules.InsideToleranceMeters)
                || VerticesTurnAngleExceeds(vertices, validation.RoadTurnAngleDegrees))
            {
                dropped++;
                continue;
            }

            var roadId = Guid.CreateVersion7();
            var roadData = DraftGeometry.ToRoadData(draft.GeometryGeoJson);
            roadData["coordinates"] = ToJsonCoordinates(vertices);
            var entity = FeatureTypeRegistry.CreateEntity(
                FeatureTypes.Road, roadId, userId, FeatureTypes.RoadLayers.Street, string.Empty, roadData.ToJsonString(), now)
                ?? throw new InvalidOperationException("FeatureTypeRegistry has no Road descriptor");
            FeatureTypeRegistry.AddToDbContext(db, entity);
            db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });

            created.Add(new GeneratedRoad(roadId, FeatureTypes.RoadLayers.Street, string.Empty, roadData));
            acceptedIds.Add(draft.Id);
        }

        if (created.Count == 0)
        {
            return new RoadGenerationSummary([], dropped);
        }

        var affected = await TransitionDraftsToAcceptedAsync(db, acceptedIds, userId, now, ct);
        if (affected < acceptedIds.Count)
        {
            // A concurrent review transitioned some drafts between our read and
            // here; do not let their roads commit as duplicates.
            return new RoadGenerationSummary([], dropped + acceptedIds.Count - affected);
        }

        await db.SaveChangesAsync(ct);
        return new RoadGenerationSummary(created, dropped);
    }

    /// <summary>
    /// Marks the consumed drafts accepted as a single conditional UPDATE ...
    /// WHERE Status = 'pending'. Kept virtual so unit tests can substitute an
    /// equivalent tracked update for the InMemory provider, which does not
    /// implement ExecuteUpdateAsync.
    /// </summary>
    protected virtual async Task<int> TransitionDraftsToAcceptedAsync(
        AppDbContext db, IReadOnlyList<Guid> draftIds, Guid userId, DateTime reviewedAt, CancellationToken ct)
        => await db.AiDraftFeatures
            .Where(f => draftIds.Contains(f.Id) && f.Status == AiDraftFeature.StatusPending)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(f => f.Status, AiDraftFeature.StatusAccepted)
                .SetProperty(f => f.ReviewedBy, userId)
                .SetProperty(f => f.ReviewedAt, reviewedAt), ct);

    private static async Task<List<IReadOnlyList<(double Lat, double Lng)>>> LoadUrbanAreaRingsAsync(
        AppDbContext db, int communeId, CancellationToken ct)
    {
        // Areas have no commune column; commune scope comes from their owner
        // user — the same join LoadCommuneRoadsAsync uses for roads.
        var areas = await (
            from a in db.Areas
            join u in db.Users on a.UserId equals u.Id
            where u.CommuneId == communeId
            select new { a.Layer, a.Data }
        ).ToListAsync(ct);

        var rings = new List<IReadOnlyList<(double Lat, double Lng)>>();
        foreach (var area in areas)
        {
            if (!FeatureTypes.AreaLayers.Urban.Contains(area.Layer)
                || !TryParseCoordinates(area.Data, 3, out var ring))
            {
                continue;
            }

            rings.Add(ring);
        }

        return rings;
    }

    private static async Task<List<IReadOnlyList<(double Lat, double Lng)>>> LoadRoadNetworkAsync(
        AppDbContext db, int communeId, CancellationToken ct)
    {
        var rows = await (
            from r in db.Roads
            join u in db.Users on r.UserId equals u.Id
            where u.CommuneId == communeId
            select r.Data
        ).ToListAsync(ct);

        var network = new List<IReadOnlyList<(double Lat, double Lng)>>();
        foreach (var data in rows)
        {
            if (TryParseCoordinates(data, 2, out var coords))
            {
                network.Add(coords);
            }
        }

        return network;
    }

    private static bool TryParseCoordinates(
        string data, int minCount, out IReadOnlyList<(double Lat, double Lng)> coordinates)
    {
        coordinates = [];
        try
        {
            var node = JsonNode.Parse(data);
            if (node?["coordinates"] is not JsonArray coordsArr || coordsArr.Count < minCount)
            {
                return false;
            }

            var list = new List<(double Lat, double Lng)>(coordsArr.Count);
            foreach (var c in coordsArr)
            {
                if (c is not JsonObject obj
                    || !obj.TryGetPropertyValue("lat", out var latNode) || latNode is not JsonValue latVal || !latVal.TryGetValue(out double lat)
                    || !obj.TryGetPropertyValue("lng", out var lngNode) || lngNode is not JsonValue lngVal || !lngVal.TryGetValue(out double lng))
                {
                    return false;
                }

                list.Add((lat, lng));
            }

            if (list.Count < minCount)
            {
                return false;
            }

            coordinates = list;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static double LineLengthM(IReadOnlyList<(double Lat, double Lng)> vertices)
    {
        var total = 0.0;
        for (var i = 1; i < vertices.Count; i++)
        {
            total += DraftGeometry.HaversineM(
                vertices[i - 1].Lng, vertices[i - 1].Lat, vertices[i].Lng, vertices[i].Lat);
        }

        return total;
    }

    private static bool VerticesTurnAngleExceeds(
        IReadOnlyList<(double Lat, double Lng)> vertices, double maxDegrees)
    {
        for (var i = 0; i + 2 < vertices.Count; i++)
        {
            var a = vertices[i];
            var b = vertices[i + 1];
            var c = vertices[i + 2];
            var angle = GeometryHelper.ComputeTurnAngle(a.Lat, a.Lng, b.Lat, b.Lng, c.Lat, c.Lng);
            if (angle > maxDegrees)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsWithinUrbanAreasM(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings,
        double toleranceM)
    {
        foreach (var (lat, lng) in vertices)
        {
            var nearest = double.MaxValue;
            foreach (var ring in areaRings)
            {
                nearest = Math.Min(nearest, RoadGenerationGeometry.DistanceToPolygonM(lat, lng, ring));
            }

            if (nearest > toleranceM)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ConnectEndpoints(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        double maxDistanceM)
    {
        // Snapping mutates the vertex list at both journey ends.
        if (vertices is not List<(double Lat, double Lng)> mutable)
        {
            return true;
        }

        foreach (var index in new[] { 0, mutable.Count - 1 })
        {
            var endpoint = mutable[index];
            if (!RoadGenerationGeometry.TrySnapToNetwork(
                    endpoint.Lat, endpoint.Lng, network, maxDistanceM, out var snapped))
            {
                return false;
            }

            mutable[index] = snapped;
        }

        return true;
    }

    /// <summary>
    /// Filters the commune's network down to the roads lying within
    /// <paramref name="maxDistanceM"/> of the draft's corridor (its vertex
    /// centroid — draft roads are short, so the centroid is a fair location for
    /// the whole line). Returns an empty set when the only mapped roads are far
    /// away, which makes this draft a network seed.
    /// </summary>
    private static List<IReadOnlyList<(double Lat, double Lng)>> RoadsNearDraft(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        double maxDistanceM)
    {
        if (vertices.Count == 0)
        {
            return [];
        }

        double sumLat = 0, sumLng = 0;
        foreach (var (lat, lng) in vertices)
        {
            sumLat += lat;
            sumLng += lng;
        }

        var centroidLat = sumLat / vertices.Count;
        var centroidLng = sumLng / vertices.Count;
        return network
            .Where(road => RoadGenerationGeometry.IsNearLine(centroidLat, centroidLng, road, maxDistanceM))
            .ToList();
    }

    private static JsonArray ToJsonCoordinates(IReadOnlyList<(double Lat, double Lng)> vertices)
    {
        var coordinates = new JsonArray();
        foreach (var (lat, lng) in vertices)
        {
            coordinates.Add(new JsonObject { ["lat"] = lat, ["lng"] = lng });
        }

        return coordinates;
    }
}
