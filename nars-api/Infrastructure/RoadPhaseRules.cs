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
    /// The road is shorter than <see cref="RoadRulesOptions.MinRoadLengthM"/>.
    /// The rule only prunes once a network exists: during a bootstrap pass (no
    /// roads at all yet) every segment is the first of its graph, so
    /// empty-network seeds are kept. Once a network exists any sub-minimum road
    /// is dropped regardless of proximity — a 5 m spur beside a road is noise,
    /// not topology; the weld pass re-connects genuinely dangling fragments
    /// that are long enough to be roads.
    /// </summary>
    TooShort,

    /// <summary>The draft's confidence is below <see cref="RoadRulesOptions.MinConfidence"/>.</summary>
    LowConfidence,

    /// <summary>Some vertex triple turns sharper than <see cref="ValidationOptions.RoadTurnAngleDegrees"/>.</summary>
    ExcessiveTurnAngle,

    /// <summary>Some vertex lies outside every urban area by more than the tolerance.</summary>
    OutsideUrbanArea,

    /// <summary>
    /// The road's corridor parallels or overlaps an existing network road,
    /// staying within <see cref="RoadRulesOptions.MinRoadSeparationMeters"/> of
    /// it for at least <see cref="RoadRulesOptions.MinRoadLengthM"/> in total —
    /// the same street detected twice, not a distinct road.
    /// </summary>
    TooClose,
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
    /// <param name="insideToleranceOverrideM">Optional override for the urban-containment
    /// tolerance; defaults to <paramref name="rules"/>.InsideToleranceMeters. The draft
    /// pre-filter passes a wider value so detections near the detection-bbox edge (which
    /// can sit outside a drawn polygon ring by a few tens of metres) still reach the
    /// review queue; materialization keeps the strict cadastre value.</param>
    public static RoadPhaseOutcome Evaluate(
        IReadOnlyList<(double Lat, double Lng)> vertices,
        double confidence,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> urbanRings,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> roadNetwork,
        ValidationOptions validation,
        RoadRulesOptions rules,
        bool snapEndpoints,
        double? insideToleranceOverrideM = null)
    {
        if (vertices.Count < 2)
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.TooShort, vertices);
        }

        // A short road is only removed when a network already exists: during a
        // bootstrap pass (no roads yet) every segment is the first of its graph,
        // and pruning it would prevent the first roads from ever being created.
        // Once a network exists, any sub-minimum road is dropped regardless of
        // proximity — a short stub beside a road is noise, not topology, and the
        // weld pass re-connects longer dangling fragments that merely miss the
        // network by a few tens of metres.
        if (roadNetwork.Count > 0
            && LineLengthM(vertices) < rules.MinRoadLengthM)
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

        if (!IsWithinUrbanAreasM(vertices, urbanRings, insideToleranceOverrideM ?? rules.InsideToleranceMeters))
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

                // A snap can pull both endpoints onto the same network node,
                // collapsing the road to a zero-length polyline — the min-length
                // rule already ran on the original vertices, so it is re-verified
                // on the mutated line here.
                if (LineLengthM(result) < rules.MinRoadLengthM)
                {
                    return new RoadPhaseOutcome(RoadPhaseViolation.TooShort, result);
                }
            }

            if (!IsWithinUrbanAreasM(result, urbanRings, rules.InsideToleranceMeters)
                || PostSnapTurnAngleExceeds(result, validation.RoadTurnAngleDegrees))
            {
                // A snap moved an endpoint outside an area edge or introduced an
                // acute turn at the junction — reject the mutated line.
                return new RoadPhaseOutcome(
                    PostSnapTurnAngleExceeds(result, validation.RoadTurnAngleDegrees)
                        ? RoadPhaseViolation.ExcessiveTurnAngle
                        : RoadPhaseViolation.OutsideUrbanArea,
                    result);
            }
        }

        // Minimum separation: a candidate that runs alongside a single network
        // road for a road-length worth of corridor is the same street detected
        // twice (the roads-phase engine's own duplicate guard). Measured on the
        // mutated result so a snap that pulls a road onto an existing corridor
        // is caught; runs whether or not endpoints were snapped, so it also
        // filters duplicate detections at the draft pre-filter.
        var maxAlongside = MaxAlongsideLengthM(result, roadNetwork, rules.MinRoadSeparationMeters);
        if (maxAlongside >= rules.MinRoadLengthM)
        {
            return new RoadPhaseOutcome(RoadPhaseViolation.TooClose, result);
        }

        return new RoadPhaseOutcome(RoadPhaseViolation.None, result);
    }

    /// <summary>
    /// Longest extent of <paramref name="candidate"/> that lies within
    /// <paramref name="maxDistanceM"/> of any single road of the network — the
    /// alongside length against that one road. Roads too far from the candidate
    /// to matter are skipped by a line-line distance quick reject; the winner's
    /// extent is what a re-detected parallel/overlapping street would report,
    /// while a junction approach stays short regardless of how many roads the
    /// candidate crosses nearby. 0 when <paramref name="network"/> carries
    /// nothing to measure against (empty bootstrap).
    /// </summary>
    private static double MaxAlongsideLengthM(
        IReadOnlyList<(double Lat, double Lng)> candidate,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        double maxDistanceM)
    {
        if (candidate.Count < 2 || network.Count == 0 || maxDistanceM <= 0.0)
        {
            return 0.0;
        }

        var best = 0.0;
        foreach (var road in network)
        {
            if (RoadGenerationGeometry.DistanceBetweenLinesM(candidate, road) >= maxDistanceM)
            {
                continue;
            }

            var along = AlongsideLengthM(candidate, road, maxDistanceM);
            if (along > best)
            {
                best = along;
            }
        }

        return best;
    }

    /// <summary>
    /// Total geodesic length of <paramref name="candidate"/> lying within
    /// <paramref name="maxDistanceM"/> of <paramref name="road"/>, estimated by
    /// sampling the corridor every metre (plenty for a 3.5 m band over roads
    /// measured in tens of metres).
    /// </summary>
    private static double AlongsideLengthM(
        IReadOnlyList<(double Lat, double Lng)> candidate,
        IReadOnlyList<(double Lat, double Lng)> road,
        double maxDistanceM)
    {
        const double stepM = 1.0;
        var along = 0.0;
        for (var i = 0; i + 1 < candidate.Count; i++)
        {
            var a = candidate[i];
            var b = candidate[i + 1];
            var segmentStart = a;
            var segLen = DraftGeometry.HaversineM(a.Lng, a.Lat, b.Lng, b.Lat);
            if (segLen <= 1e-9)
            {
                continue;
            }

            var steps = Math.Max(1, (int)Math.Ceiling(segLen / stepM));
            for (var s = 0; s < steps; s++)
            {
                var t = (s + 0.5) / steps;
                var lat = segmentStart.Lat + t * (b.Lat - segmentStart.Lat);
                var lng = segmentStart.Lng + t * (b.Lng - segmentStart.Lng);
                if (RoadGenerationGeometry.IsNearLine(lat, lng, road, maxDistanceM))
                {
                    along += segLen / steps;
                }
            }
        }

        return along;
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

    /// <summary>
    /// Turn-angle re-check after endpoint snapping. The pre-snap line already
    /// passed <see cref="VerticesTurnAngleExceeds"/>; a snap only ever moves the
    /// line's first and/or last vertex, so only the triples touching those ends
    /// can newly exceed the limit. Re-testing the entire line would let a
    /// legitimately straight fragment be rejected because the junction it snaps
    /// into reads as an acute turn — the two end triples are the whole, honest
    /// scope of the "did the snap introduce a U-turn" question. Returns true
    /// when either end triple exceeds <paramref name="maxDegrees"/>.
    /// </summary>
    private static bool PostSnapTurnAngleExceeds(
        IReadOnlyList<(double Lat, double Lng)> snapped, double maxDegrees)
        => TripleTurnAngleExceeds(snapped, 0, maxDegrees)
            || TripleTurnAngleExceeds(snapped, snapped.Count - 3, maxDegrees);

    /// <summary>The turn angle at the (i+1)-th vertex exceeds <paramref name="maxDegrees"/>.</summary>
    private static bool TripleTurnAngleExceeds(
        IReadOnlyList<(double Lat, double Lng)> vertices, int i, double maxDegrees)
    {
        if (i < 0 || i + 2 >= vertices.Count)
        {
            return false;
        }

        var a = vertices[i];
        var b = vertices[i + 1];
        var c = vertices[i + 2];
        return GeometryHelper.ComputeTurnAngle(a.Lat, a.Lng, b.Lat, b.Lng, c.Lat, c.Lng) > maxDegrees;
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
}
