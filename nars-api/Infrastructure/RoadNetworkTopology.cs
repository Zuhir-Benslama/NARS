using NarsApi.Infrastructure;

namespace NarsApi.Infrastructure;

/// <summary>A merged road: the concatenated vertex list plus the original
/// candidate indices it was built from (so the caller can reassociate the draft
/// metadata after a merge).</summary>
public sealed record MergedComponent(
    IReadOnlyList<(double Lat, double Lng)> Vertices,
    IReadOnlyList<int> MemberIndices);

/// <summary>
/// Post-generation network topology for the roads phase: welding dangling road
/// endpoints onto the commune's road network and fusing collinear roads that
/// were split at shared junctions back into one feature. Pure and stateless —
/// every input polyline is passed in and the results are returned, matching the
/// RoadGenerationGeometry pattern so both passes are unit-testable without a
/// database.
///
/// Welding: for each dangling endpoint (one that does not already touch the
/// network within a metre), try a straight extension along the road's final
/// bearing first — it stops at the first network road it meets (a T-junction,
/// never a pierce) — and fall back to a nearest-point projection onto the
/// network. Every weld is validated against the same conventions as the cadastre
/// rules (turn angle at the end triples, urban containment, minimum length, no
/// new interior crossing) and skipped when it would violate any of them.
/// Iterates until no endpoint can weld: each successful weld pins an endpoint
/// onto the network (distance → 0), so termination is guaranteed; iteration only
/// lets an extension made elsewhere in the same pass pull a drifted endpoint
/// into range.
///
/// Merging: roads that share an endpoint (gap ≤ 1 m) and continue on a nearly
/// straight line (≤ 15°) are one physical road that <see cref="RoadGenerationGeometry.SplitLineAtCrossings"/>
/// cut at a shared junction — concatenate them back into a single polyline.
/// </summary>
public static class RoadNetworkTopology
{
    /// <summary>Endpoint gap under which two roads count as sharing a junction.</summary>
    public const double MergeEndpointGapM = 1.0;

    /// <summary>Maximum divergence from a straight line that still counts as collinear.</summary>
    public const double MergeCollinearDegrees = 15.0;

    /// <summary>
    /// Maximum a member's interior vertices may stray from the established chain
    /// corridor and still count as duplicate coverage of that corridor. A member
    /// whose far end closes back onto the chain and whose interior stays within
    /// this deviation is a re-draw of the same road — it must not be spliced in
    /// (that would fold the polyline back on itself). A member whose interior
    /// bends further away is a genuine loop/ring and stays allowed.
    /// </summary>
    private const double FoldMaxDeviationM = 10.0;

    /// <summary>Hard cap on weld iterations; termination is already guaranteed, this is a safety net.</summary>
    private const int MaxWeldPasses = 10;

    /// <summary>Distance under which an endpoint is already connected to the network.</summary>
    private const double TouchingToleranceM = 1.0;

    /// <summary>
    /// Welds every dangling endpoint of <paramref name="candidates"/> onto
    /// <paramref name="network"/>. Only the candidate polylines are mutated (in
    /// place); the pre-existing network roads are anchors and never edited. The
    /// candidates are expected to be the same <see cref="List{T}"/> objects also
    /// referenced inside <paramref name="network"/>, so a mutation here is
    /// visible to the caller's network snapshot. Returns the number of endpoints
    /// welded.
    /// </summary>
    public static int WeldEndpoints(
        IReadOnlyList<List<(double Lat, double Lng)>> candidates,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings,
        ValidationOptions validation,
        RoadRulesOptions rules)
    {
        var welded = 0;
        for (var pass = 0; pass < MaxWeldPasses; pass++)
        {
            var anyWeld = false;
            foreach (var road in candidates)
            {
                var before = welded;
                TryWeldRoad(road, network, areaRings, validation, rules, ref welded);
                anyWeld |= welded > before;
            }

            if (!anyWeld)
            {
                break;
            }
        }

        return welded;
    }

    /// <summary>
    /// Tries to weld both endpoints of one road; mutates <paramref name="road"/>
    /// in place when a weld succeeds. Increments <paramref name="welded"/> by the
    /// number of endpoints actually welded.
    /// </summary>
    private static void TryWeldRoad(
        List<(double Lat, double Lng)> road,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings,
        ValidationOptions validation,
        RoadRulesOptions rules,
        ref int welded)
    {
        if (road.Count < 2)
        {
            return;
        }

        // The road must not weld onto itself — its own body is not a target.
        var others = network.Where(r => !ReferenceEquals(r, road)).ToList();
        if (others.Count == 0)
        {
            return;
        }

        foreach (var index in new[] { road.Count - 1, 0 })
        {
            var endpoint = road[index];
            if (RoadGenerationGeometry.TrySnapToNetwork(
                    endpoint.Lat, endpoint.Lng, others, TouchingToleranceM, out _))
            {
                // Already connected (or within a metre) — nothing to weld.
                continue;
            }

            var prev = index == 0 ? road[1] : road[road.Count - 2];

            // 1. Prefer a straight continuation: march the final bearing to the
            //    nearest network crossing, which becomes a T-junction.
            var extended = new List<(double Lat, double Lng)>(road);
            (double Lat, double Lng)? weldTarget = null;
            if (RoadGenerationGeometry.TryExtendToNetwork(
                    endpoint.Lat, endpoint.Lng, prev.Lat, prev.Lng,
                    others, rules.RoadWeldRadiusM, out var hit))
            {
                if (index == 0)
                {
                    extended.Insert(0, hit);
                }
                else
                {
                    extended.Add(hit);
                }

                weldTarget = hit;
            }

            if (weldTarget is not null && WeldIsValid(extended, areaRings, validation, rules))
            {
                ApplyInPlace(road, extended);
                welded++;
                continue;
            }

            // 2. Fall back to the nearest network point (a T-junction), provided
            //    the moved endpoint does not make the road pierce another road.
            if (RoadGenerationGeometry.TrySnapToNetwork(
                    endpoint.Lat, endpoint.Lng, others, rules.RoadWeldRadiusM, out var snapped))
            {
                var projected = new List<(double Lat, double Lng)>(road);
                projected[index] = snapped;

                if (WeldIsValid(projected, areaRings, validation, rules)
                    && !RoadGenerationGeometry.ProperlyCrossesAnyNetworkSegment(projected, others))
                {
                    ApplyInPlace(road, projected);
                    welded++;
                }
            }
        }
    }

    private static void ApplyInPlace(
        List<(double Lat, double Lng)> road,
        IReadOnlyList<(double Lat, double Lng)> newVertices)
    {
        road.Clear();
        road.AddRange(newVertices);
    }

    private static bool WeldIsValid(
        IReadOnlyList<(double Lat, double Lng)> line,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> areaRings,
        ValidationOptions validation,
        RoadRulesOptions rules)
    {
        if (line.Count < 2)
        {
            return false;
        }

        // Minimum length (the network always exists here — we are welding onto it).
        if (LineLengthM(line) < rules.MinRoadLengthM)
        {
            return false;
        }

        // A weld must not fold the road back on itself: the only vertices a weld
        // touches are the end triples, so test exactly those two windows, the
        // same honest scope the cadastre rule uses after endpoint snapping.
        if (TripleTurnAngleExceeds(line, 0, validation.RoadTurnAngleDegrees)
            || TripleTurnAngleExceeds(line, line.Count - 3, validation.RoadTurnAngleDegrees))
        {
            return false;
        }

        // The new vertex (and the whole line) must stay within an urban area.
        foreach (var (lat, lng) in line)
        {
            var nearest = double.MaxValue;
            foreach (var ring in areaRings)
            {
                nearest = Math.Min(nearest, RoadGenerationGeometry.DistanceToPolygonM(lat, lng, ring));
            }

            if (nearest > rules.InsideToleranceMeters)
            {
                return false;
            }
        }

        return true;
    }

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

    /// <summary>
    /// Fuses collinear roads that share an endpoint into single polylines.
    /// Returns one merged component per connected group; each is the
    /// concatenation of its chain's vertex lists with the shared junction
    /// points deduplicated, plus the indices of the candidate roads it came
    /// from (so the caller can recover per-draft data after merging). Roads that
    /// share an endpoint but bend more than <paramref name="maxDegrees"/> form a
    /// real junction, not a split road, and are left alone. The
    /// <paramref name="candidates"/> list itself is not modified — new vertex
    /// lists are returned.
    /// </summary>
    public static IReadOnlyList<MergedComponent> MergeCollinearRoads(
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> candidates,
        double toleranceM = MergeEndpointGapM,
        double maxDegrees = MergeCollinearDegrees)
    {
        var count = candidates.Count;
        if (count == 0)
        {
            return [];
        }

        if (count == 1)
        {
            return [new MergedComponent(candidates[0], [0])];
        }

        var parent = new int[count];
        var rank = new int[count];
        for (var i = 0; i < count; i++)
        {
            parent[i] = i;
        }

        int Find(int x)
        {
            while (parent[x] != x)
            {
                parent[x] = parent[parent[x]];
                x = parent[x];
            }

            return x;
        }

        void Union(int a, int b)
        {
            var ra = Find(a);
            var rb = Find(b);
            if (ra == rb)
            {
                return;
            }

            if (rank[ra] < rank[rb])
            {
                (ra, rb) = (rb, ra);
            }

            parent[rb] = ra;
            if (rank[ra] == rank[rb])
            {
                rank[ra]++;
            }
        }

        for (var i = 0; i < count; i++)
        {
            for (var j = i + 1; j < count; j++)
            {
                if (AreCollinearTouching(candidates[i], candidates[j], toleranceM, maxDegrees))
                {
                    Union(i, j);
                }
            }
        }

        // Group the indices, then assemble each group's chain in order.
        var groups = new Dictionary<int, List<int>>();
        for (var i = 0; i < count; i++)
        {
            var root = Find(i);
            if (!groups.TryGetValue(root, out var list))
            {
                list = [];
                groups[root] = list;
            }

            list.Add(i);
        }

        var result = new List<MergedComponent>(groups.Count);
        foreach (var group in groups.Values)
        {
            var memberIndices = group.ToArray();
            if (group.Count == 1)
            {
                result.Add(new MergedComponent(candidates[group[0]], memberIndices));
                continue;
            }

            var chain = BuildChain(group.Select(i => candidates[i]).ToList(), toleranceM);
            if (chain is not null && !HasFold(chain, toleranceM))
            {
                result.Add(new MergedComponent(chain, memberIndices));
            }
            else
            {
                // The corridor cannot be concatenated without folding back on
                // itself or duplicating it (same physical road detected twice,
                // e.g. one fragment overlapping another near-clone). No single
                // polyline represents the group honestly — keep the members as
                // their original separate roads rather than materialize a
                // collapsed loop.
                foreach (var i in group)
                {
                    result.Add(new MergedComponent(candidates[i], [i]));
                }
            }
        }

        return result;
    }

    private static bool AreCollinearTouching(
        IReadOnlyList<(double Lat, double Lng)> a,
        IReadOnlyList<(double Lat, double Lng)> b,
        double toleranceM,
        double maxDegrees)
    {
        if (a.Count < 2 || b.Count < 2)
        {
            return false;
        }

        // Share an endpoint within tolerance: an end of either road hits the
        // other road (its interior counts — a T-junction or a stub lying on a
        // longer body). Both directions are tested so the union does not depend
        // on which road happens to come first in index order.
        var touches = RoadGenerationGeometry.TrySnapToNetwork(a[0].Lat, a[0].Lng, [b], toleranceM, out _)
            || RoadGenerationGeometry.TrySnapToNetwork(a[^1].Lat, a[^1].Lng, [b], toleranceM, out _)
            || RoadGenerationGeometry.TrySnapToNetwork(b[0].Lat, b[0].Lng, [a], toleranceM, out _)
            || RoadGenerationGeometry.TrySnapToNetwork(b[^1].Lat, b[^1].Lng, [a], toleranceM, out _);
        if (!touches)
        {
            return false;
        }

        // Collinearity: the two through-directions compared end-to-end must
        // diverge from a straight continuation by less than the threshold. A
        // meander inside either road doesn't matter — only the corridor.
        var divergence = Math.Abs(DirectionDifference(a, b));
        return divergence <= maxDegrees;
    }

    private static double DirectionDifference(
        IReadOnlyList<(double Lat, double Lng)> a,
        IReadOnlyList<(double Lat, double Lng)> b)
    {
        var a1 = BearingDeg(a[0], a[^1]);
        var b1 = BearingDeg(b[0], b[^1]);

        // Undirected line separation: reduce the raw bearing difference to
        // [0, 180] (roads heading the same way and roads heading opposite ways
        // on the same line are both collinear), then reflect > 90° into the
        // [0, 90] wedge. A naive "180 - diff" on an unwrapped difference — like
        // the 355° wraparound of two roads 5° apart — reported ~175° and kept
        // genuinely straight continuations from ever merging.
        var diff = ModDeg(a1 - b1);
        if (diff > 180.0)
        {
            diff = 360.0 - diff;
        }

        return diff > 90.0 ? 180.0 - diff : diff;
    }

    private static double BearingDeg((double Lat, double Lng) from, (double Lat, double Lng) to)
        => Math.Atan2(to.Lng - from.Lng, to.Lat - from.Lat) * (180.0 / Math.PI);

    private static double ModDeg(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }

    /// <summary>
    /// Orders a group of collinear, endpoint-sharing roads into one connected
    /// chain and concatenates their vertex lists, dropping the duplicated
    /// junction point between neighbours. The chain is built greedily from both
    /// open ends, which is exact for the straight corridors these roads follow.
    ///
    /// Three guards protect the assembly from the failure mode this code used to
    /// hit — roads that share only an endpoint yet cover the SAME corridor (a
    /// near-identical duplicate of an already-merged road):
    /// <list type="bullet">
    /// <item>The chain is seeded with the longest member (ties: more vertices,
    /// then lower index), so the true corridor spine dominates and an index-first
    /// short fragment cannot anchor the assembly.</item>
    /// <item>A member is only spliced at a chain open end when one of its
    /// <em>own endpoints</em> actually lies on that end — a road that merely
    /// spans or passes through the anchor is not a continuation.</item>
    /// <item>A member whose far end closes back onto the chain is a loop: it is
    /// rejected unless its interior genuinely departs from the chain corridor
    /// (a real ring road), keeping near-duplicate roads from folding the merged
    /// polyline back on itself.</item>
    /// </list>
    /// Returns null when a member could not be placed without duplicating or
    /// folding the corridor — the caller keeps the members as separate roads.
    /// </summary>
    private static List<(double Lat, double Lng)>? BuildChain(
        List<IReadOnlyList<(double Lat, double Lng)>> members,
        double toleranceM)
    {
        var used = new HashSet<int>();
        var seed = PickSeed(members);
        used.Add(seed);

        var chain = new List<(double Lat, double Lng)>(members[seed]);
        var openA = chain[0];
        var openB = chain[^1];

        while (used.Count < members.Count)
        {
            var advanced = false;
            for (var i = 0; i < members.Count; i++)
            {
                if (used.Contains(i))
                {
                    continue;
                }

                // Append at openB, dropping the shared junction vertex.
                if (ExtendChain(members[i], openB, chain, toleranceM))
                {
                    openB = chain[^1];
                    used.Add(i);
                    advanced = true;
                    break;
                }

                // Prepend at openA: splice the member's vertices in front so its
                // far end becomes the new open end.
                if (ExtendChain(members[i], openA, chain, toleranceM, prepend: true))
                {
                    openA = chain[0];
                    used.Add(i);
                    advanced = true;
                    break;
                }
            }

            if (!advanced)
            {
                // A member joined the group (endpoint tolerance) but cannot be
                // placed without duplicating or folding the corridor. Fall back
                // to separate roads rather than silently dropping it.
                return null;
            }
        }

        return chain;
    }

    /// <summary>Picks the seed member: the longest; ties broken by vertex count,
    /// then by lower index (deterministic on the caller's ordering).</summary>
    private static int PickSeed(List<IReadOnlyList<(double Lat, double Lng)>> members)
    {
        var best = 0;
        var bestLength = LineLengthM(members[0]);
        for (var i = 1; i < members.Count; i++)
        {
            var length = LineLengthM(members[i]);
            var better = length > bestLength + 1e-9
                || (Math.Abs(length - bestLength) <= 1e-9 && members[i].Count > members[best].Count);
            if (better)
            {
                best = i;
                bestLength = length;
            }
        }

        return best;
    }

    /// <summary>
    /// Splices <paramref name="member"/> onto <paramref name="chain"/> at
    /// <paramref name="anchor"/> (an open chain end), dropping the shared
    /// junction vertex. Appends the member's remaining vertices after the chain
    /// unless <paramref name="prepend"/> is set, in which case they are reversed
    /// in front. Applies only to a copy, so a rejected splice leaves the chain
    /// untouched.
    /// </summary>
    private static bool ExtendChain(
        IReadOnlyList<(double Lat, double Lng)> member,
        (double Lat, double Lng) anchor,
        List<(double Lat, double Lng)> chain,
        double toleranceM,
        bool prepend = false)
    {
        // Junction fidelity: the anchor must be one of the member's own
        // endpoints. Testing the member as a whole would let a road whose body
        // merely passes through the open end get spliced in as a continuation.
        var squaredTolerance = toleranceM * toleranceM;
        if (SquaredDistanceM(member[0], anchor) > squaredTolerance
            && SquaredDistanceM(member[^1], anchor) > squaredTolerance)
        {
            return false;
        }

        var ordered = OrderToward(member, anchor);
        if (SquaredDistanceM(ordered[0], anchor) > squaredTolerance)
        {
            return false;
        }

        // Fold guard: the member's far end closing onto the chain means this
        // splice would loop the polyline back onto itself. Tolerate it only for
        // a genuine ring, whose interior swings away from the chain corridor.
        if (ChainDistanceM(ordered[^1], chain) <= toleranceM && !IsGenuineRing(member, chain))
        {
            return false;
        }

        var candidate = new List<(double Lat, double Lng)>(chain.Count + ordered.Count - 1);
        if (prepend)
        {
            candidate.AddRange(ordered.Skip(1).Reverse());
            candidate.AddRange(chain);
        }
        else
        {
            candidate.AddRange(chain);
            candidate.AddRange(ordered.Skip(1));
        }

        chain.Clear();
        chain.AddRange(candidate);
        return true;
    }

    /// <summary>
    /// True when a member whose far end closes onto the chain is a genuine ring
    /// rather than a near-duplicate of the corridor: it must have interior
    /// vertices, and at least one must depart from the chain corridor by more
    /// than <see cref="FoldMaxDeviationM"/>. A two-vertex member (empty
    /// interior) with both ends on the chain is a pure duplicate segment.
    /// </summary>
    private static bool IsGenuineRing(
        IReadOnlyList<(double Lat, double Lng)> member,
        IReadOnlyList<(double Lat, double Lng)> chain)
    {
        if (member.Count < 3)
        {
            return false;
        }

        for (var i = 1; i < member.Count - 1; i++)
        {
            if (ChainDistanceM(member[i], chain) > FoldMaxDeviationM)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// True when two non-adjacent vertices of the chain coincide within
    /// tolerance — a folded/collapsed polyline that must not be materialized.
    /// </summary>
    private static bool HasFold(IReadOnlyList<(double Lat, double Lng)> chain, double toleranceM)
    {
        var squaredTolerance = toleranceM * toleranceM;
        for (var i = 0; i < chain.Count; i++)
        {
            for (var j = i + 2; j < chain.Count; j++)
            {
                if (SquaredDistanceM(chain[i], chain[j]) <= squaredTolerance)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>Shortest distance in metres from a point to a polyline
    /// (0 for a point on any segment), in the local equirectangular plane.</summary>
    private static double ChainDistanceM(
        (double Lat, double Lng) point,
        IReadOnlyList<(double Lat, double Lng)> chain)
    {
        var cosLat = Math.Cos(point.Lat * Math.PI / 180.0);
        var minSquared = double.MaxValue;
        for (var i = 0; i + 1 < chain.Count; i++)
        {
            var dSquared = SquaredSegmentDistanceM(
                point.Lat, point.Lng, chain[i].Lat, chain[i].Lng, chain[i + 1].Lat, chain[i + 1].Lng, cosLat);
            if (dSquared < minSquared)
            {
                minSquared = dSquared;
            }
        }

        return Math.Sqrt(minSquared);
    }

    /// <summary>Orders a member so its end nearest <paramref name="anchor"/>
    /// (an open chain end) is first, so the chain grows away from the anchor.</summary>
    private static List<(double Lat, double Lng)> OrderToward(
        IReadOnlyList<(double Lat, double Lng)> member,
        (double Lat, double Lng) anchor)
    {
        var dStart = SquaredDistanceM(member[0], anchor);
        var dEnd = SquaredDistanceM(member[^1], anchor);
        return dStart <= dEnd ? [.. member] : [.. member.Reverse()];
    }

    private static double SquaredDistanceM((double Lat, double Lng) a, (double Lat, double Lng) b)
    {
        var cosLat = Math.Cos(a.Lat * Math.PI / 180.0);
        var dLat = (b.Lat - a.Lat) * 111_320.0;
        var dLng = (b.Lng - a.Lng) * cosLat * 111_320.0;
        return dLat * dLat + dLng * dLng;
    }

    private static double SquaredSegmentDistanceM(
        double lat, double lng, double aLat, double aLng, double bLat, double bLng, double cosLat)
    {
        var ax = (aLng - lng) * cosLat * 111_320.0;
        var ay = (aLat - lat) * 111_320.0;
        var bx = (bLng - lng) * cosLat * 111_320.0;
        var by = (bLat - lat) * 111_320.0;

        var dx = bx - ax;
        var dy = by - ay;
        var lenSq = dx * dx + dy * dy;
        if (lenSq == 0.0)
        {
            return ax * ax + ay * ay;
        }

        var t = Math.Clamp(-(ax * dx + ay * dy) / lenSq, 0.0, 1.0);
        var px = ax + t * dx;
        var py = ay + t * dy;
        return px * px + py * py;
    }
}
