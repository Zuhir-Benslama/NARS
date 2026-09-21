using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

/// <summary>
/// The single set of roads-phase cadastre rules every road the system
/// materializes must satisfy (bulk generate-roads, individual draft accepts,
/// and the segmentation draft pre-filter): minimum geodesic length (when a
/// network exists), minimum confidence, maximum turn angle, urban containment,
/// and endpoint snapping onto the commune's locally present road network. One
/// engine, three call sites — a rule configured (or a decision changed) here
/// applies everywhere, so no path can promote a road the phase rules reject.
/// </summary>
public enum RoadPhaseViolation
{
    /// <summary>No violation; the road may be materialized.</summary>
    None,

    /// <summary>
    /// The road is shorter than <see cref="RoadRulesOptions.MinRoadLengthM"/>
    /// and isolated — farther than <see cref="RoadRulesOptions.RoadIsolationMeters"/>
    /// from every road of the network. The rule only prunes once a network
    /// exists: during a bootstrap pass (no roads at all yet) every short
    /// segment would be trivially "isolated", which would prevent the first
    /// roads from ever being created, so empty-network seeds are kept. A short
    /// stub that touches or sits within the isolation distance of a road is
    /// kept: it is a connection, not a spur.
    /// </summary>
    TooShort,

    /// <summary>The draft's confidence is below <see cref="RoadRulesOptions.MinConfidence"/>.</summary>
    LowConfidence,

    /// <summary>Some vertex triple turns sharper than <see cref="ValidationOptions.RoadTurnAngleDegrees"/>.</summary>
    ExcessiveTurnAngle,

    /// <summary>Some vertex lies outside every urban area by more than the tolerance.</summary>
    OutsideUrbanArea,
}

/// <summary>
/// Outcome of a roads-phase evaluation. <see cref="Coordinates"/> is the
/// original vertex list when no snapping applied (or when the road violates a
/// rule), and the network-snapped vertex list when <c>snapEndpoints</c> was
/// requested and connectivity passed — the caller materializes exactly these
/// coordinates.
/// </summary>
public sealed record RoadPhaseOutcome(
    RoadPhaseViolation Violation,
    IReadOnlyList<(double Lat, double Lng)> Coordinates);

/// <summary>Implements the roads-phase rules. See the enum for the rule set.</summary>
public static class RoadPhaseRules
{
    /// <summary>
    /// Evaluates one candidate road (a parsed vertex list) against the phase
    /// rules. The check order mirrors the long-standing generate-roads order:
    /// length, confidence, turn angle, urban containment, then endpoint
    /// snapping (when <paramref name="snapEndpoints"/> is true), which
    /// re-verifies containment and turn angle on the mutated line so a
    /// pathological snap can still reject the road. Connectivity is a merge
    /// operation, not a rejection: endpoints within the connectivity distance
    /// are snapped onto the locally present network, while a road with no
    /// nearby network simply seeds it — nothing is dropped for being far from
    /// an existing road, because every materialized road is part of the
    /// commune's growing graph.
    /// </summary>
    /// <param name="vertices">The draft's parsed (Lat, Lng) vertices, count ≥ 2.</param>
    /// <param name="confidence">The draft's confidence in [0, 1].</param>
    /// <param name="urbanRings">Urban-area polygon rings, or empty when the commune has none.</param>
    /// <param name="roadNetwork">The commune's existing road polylines.</param>
    /// <param name="validation">Turn-angle and connectivity thresholds.</param>
    /// <param name="rules">Length, confidence, containment-tolerance and search-radius thresholds.</param>
    /// <param name="snapEndpoints">True to snap connected endpoints onto the network (materialization);
    /// false keeps the original vertices (draft pre-filtering only).</param>
    public static RoadPhaseOutcome Evaluate(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        double confidence,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> urbanRings,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> roadNetwork,
        ValidationOptions validation,
        RoadRulesOptions rules,
        bool snapEndpoints)
    {
        if (vertices.Count < 2)
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.TooShort, vertices);
        }

        // A short road is only removed when it is also isolated AND a network
        // already exists: a sub-minimum spur that touches a road (or sits
        // within the isolation distance) is a connection, not a delete
        // candidate, and during a bootstrap pass (no roads yet) every short
        // segment would be trivially "isolated" — pruning it would prevent the
        // first roads from ever being created. Distance to the network is 0 for
        // a touching/shared-junction road, so the nearest road decides.
        if (roadNetwork.Count > 0
            && LineLengthM(vertices) < rules.MinRoadLengthM
            && IsIsolatedFromNetwork(vertices, roadNetwork, rules.RoadIsolationMeters))
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.TooShort, vertices);
        }

        if (confidence < rules.MinConfidence)
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.LowConfidence, vertices);
        }

        if (VerticesTurnAngleExceeds(vertices, validation.RoadTurnAngleDegrees))
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.ExcessiveTurnAngle, vertices);
        }

        if (!IsWithinUrbanAreasM(vertices, urbanRings, rules.InsideToleranceMeters))
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.OutsideUrbanArea, vertices);
        }

        // Endpoint snapping (the merge half of connectivity): endpoints within
        // the connectivity distance of a locally present road are snapped onto
        // it, becoming part of the same graph. A road with no local network (or
        // with endpoints beyond the tolerance) keeps its coordinates and simply
        // seeds/extends the network — connectivity never rejects a road here,
        // otherwise generation from an empty commune could never get started.
        var result = vertices;
        if (snapEndpoints)
        {
            var localNetwork = RoadsNearDraft(vertices, roadNetwork, rules.RoadNetworkSearchMeters);
            if (localNetwork.Count > 0)
            {
                result = SnapEndpoints(vertices, localNetwork, validation.RoadConnectivityMeters);
            }

            if (!IsWithinUrbanAreasM(result, urbanRings, rules.InsideToleranceMeters)
                || VerticesTurnAngleExceeds(result, validation.RoadTurnAngleDegrees))
            {
                // A snap moved an endpoint outside an area edge or introduced an
                // acute turn at the junction — reject the mutated line.
                return new RoadPhaseOutcome(
                    VerticesTurnAngleExceeds(result, validation.RoadTurnAngleDegrees)
                        ? RoadPhaseViolation.ExcessiveTurnAngle
                        : RoadPhaseViolation.OutsideUrbanArea,
                    result);
            }
        }

        return new RoadPhaseOutcome(RoadPhaseViolation.None, result);
    }

    /// <summary>Serializes a vertex list as the production <c>coordinates</c> [{lat, lng}, ...].</summary>
    public static JsonArray ToJsonCoordinates(IReadOnlyList<(double Lat, double Lng)> vertices)
    {
        var coordinates = new JsonArray();
        foreach (var (lat, lng) in vertices)
        {
            coordinates.Add(new JsonObject { ["lat"] = lat, ["lng"] = lng });
        }

        return coordinates;
    }

    /// <summary>
    /// Urban polygons for a commune: areas (central/secondary urban layers only),
    /// scope resolved through their owner user, same join the road network uses.
    /// </summary>
    public static async Task<List<IReadOnlyList<(double Lat, double Lng)>>> LoadUrbanAreaRingsAsync(
        AppDbContext db, int communeId, CancellationToken ct)
    {
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

    /// <summary>Every road in the commune (scope via owner user), as vertex lists.</summary>
    public static async Task<List<IReadOnlyList<(double Lat, double Lng)>>> LoadRoadNetworkAsync(
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

    /// <summary>
    /// The merge half of connectivity: replaces each endpoint of
    /// <paramref name="vertices"/> by its projection onto the local
    /// <paramref name="network"/> when the nearest network point lies within
    /// <paramref name="maxDistanceM"/>. Endpoints beyond the tolerance are left
    /// untouched — a road that cannot reach the network is not dropped, it
    /// simply extends the commune's graph as a seed.
    /// </summary>
    private static IReadOnlyList<(double Lat, double Lng)> SnapEndpoints(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        double maxDistanceM)
    {
        var changed = new List<(double Lat, double Lng)>(vertices);
        foreach (var index in new[] { 0, vertices.Count - 1 })
        {
            var endpoint = vertices[index];
            if (RoadGenerationGeometry.TrySnapToNetwork(
                    endpoint.Lat, endpoint.Lng, network, maxDistanceM, out var snapped))
            {
                changed[index] = snapped;
            }
        }

        return changed;
    }

    /// <summary>
    /// Filters the commune's network down to the roads lying within
    /// <paramref name="maxDistanceM"/> of the candidate's corridor (its vertex
    /// centroid — candidate roads are short, so the centroid is a fair location
    /// for the whole line). Empty when the only mapped roads are far away
    /// (legacy/demo roads in another district), in which case there is nothing
    /// local to snap onto and the candidate is simply kept as a network seed.
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

    /// <summary>
    /// True when <em>no</em> road of the network lies within
    /// <paramref name="isolationMeters"/> of the candidate — the second half of
    /// the minimum-length rule. Distance to a touching road is 0, so candidates
    /// that share a junction with the network are never isolated.
    /// </summary>
    private static bool IsIsolatedFromNetwork(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> roadNetwork,
        double isolationMeters)
        => !roadNetwork.Any(road =>
            RoadGenerationGeometry.DistanceBetweenLinesM(vertices, road) <= isolationMeters);
}
