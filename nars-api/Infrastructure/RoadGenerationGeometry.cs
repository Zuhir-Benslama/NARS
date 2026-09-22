namespace NarsApi.Infrastructure;

/// <summary>
/// Pure planar-geometry helpers for AI road generation: urban containment and
/// metre-accurate endpoint snapping onto the commune's road network. No state,
/// no PostGIS — the same independently testable pattern as GeometryHelper and
/// DraftGeometry.
///
/// Distances are measured in a local equirectangular projection (metres per
/// degree of latitude constant, longitude scaled by cos(lat)). On the few-km
/// scale of a commune's urban areas this metre accuracy is more than enough for
/// the ≤ 30 m tolerances in play here.
/// </summary>
public static class RoadGenerationGeometry
{
    private const double EarthMetersPerDegreeLat = 111_320.0;

    /// <summary>
    /// Minimum length kept when <see cref="SplitLineAtCrossings"/> cuts a
    /// line into pieces. The sliver between two near-identical crossings
    /// (junction hops the skeletonizer emits) would otherwise survive as a
    /// tiny, unselectable road even though it stays connected.
    /// </summary>
    public const double SplitSliverEpsilonM = 1.0;

    /// <summary>
    /// Point-in-polygon test (ray casting) against a polygon ring in the
    /// lat/lng plane. The ring is treated as implicitly closed, matching how
    /// the front-end renders user-drawn area polygons (buildFeatureData does
    /// not repeat the first vertex; closeRing adds it at render time).
    /// </summary>
    public static bool IsPointInsidePolygon(
        double lat, double lng, IReadOnlyList<(double Lat, double Lng)> ring)
    {
        if (ring.Count < 3)
        {
            return false;
        }

        var inside = false;
        var j = ring.Count - 1;
        for (var i = 0; i < ring.Count; i++)
        {
            var (yi, xi) = ring[i];
            var (yj, xj) = ring[j];
            if ((yi > lat) != (yj > lat) &&
                lng < (xj - xi) * (lat - yi) / (yj - yi) + xi)
            {
                inside = !inside;
            }

            j = i;
        }

        return inside;
    }

    /// <summary>
    /// Distance in metres from a point to a polygon ring (in metres to the
    /// nearest edge/vertex), or 0 when the point lies inside the ring. Empty
    /// rings return 0 (no containment contribution).
    /// </summary>
    public static double DistanceToPolygonM(
        double lat, double lng, IReadOnlyList<(double Lat, double Lng)> ring)
    {
        if (ring.Count < 3)
        {
            return 0.0;
        }

        if (IsPointInsidePolygon(lat, lng, ring))
        {
            return 0.0;
        }

        var cosLat = Math.Cos(lat * Math.PI / 180.0);
        var minSquared = double.MaxValue;
        for (var i = 0; i < ring.Count; i++)
        {
            var a = ring[i];
            var b = ring[(i + 1) % ring.Count];
            var d = SquaredSegmentDistanceM(lat, lng, a.Lat, a.Lng, b.Lat, b.Lng, cosLat);
            if (d < minSquared)
            {
                minSquared = d;
            }
        }

        return Math.Sqrt(minSquared);
    }

    /// <summary>
    /// Projects a point onto the nearest segment of any road in the network
    /// (each road is a polyline of its own). Returns the snapped position and
    /// true when the nearest distance is within <paramref name="maxDistanceM"/>,
    /// so callers can reject roads that cannot be connected to the commune's
    /// existing network. When multiple roads tie, the first hit wins — snapping
    /// is connectivity-only, not road preference.
    /// </summary>
    public static bool TrySnapToNetwork(
        double lat, double lng,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        double maxDistanceM,
        out (double Lat, double Lng) snapped)
    {
        snapped = (lat, lng);
        var cosLat = Math.Cos(lat * Math.PI / 180.0);
        var bestSquared = double.MaxValue;
        (double Lat, double Lng) best = (lat, lng);

        foreach (var road in network)
        {
            for (var i = 0; i + 1 < road.Count; i++)
            {
                var a = road[i];
                var b = road[i + 1];

                // Project the origin (the point) onto segment a→b in local metres.
                var ax = (a.Lng - lng) * cosLat * EarthMetersPerDegreeLat;
                var ay = (a.Lat - lat) * EarthMetersPerDegreeLat;
                var bx = (b.Lng - lng) * cosLat * EarthMetersPerDegreeLat;
                var by = (b.Lat - lat) * EarthMetersPerDegreeLat;

                var dx = bx - ax;
                var dy = by - ay;
                var lenSq = dx * dx + dy * dy;
                double t = lenSq == 0.0 ? 0.0 : Math.Clamp(-(ax * dx + ay * dy) / lenSq, 0.0, 1.0);

                var px = ax + t * dx;
                var py = ay + t * dy;
                var dSq = px * px + py * py;
                if (dSq < bestSquared)
                {
                    bestSquared = dSq;
                    var snappedLng = lng + px / (cosLat * EarthMetersPerDegreeLat);
                    var snappedLat = lat + py / EarthMetersPerDegreeLat;
                    best = (snappedLat, snappedLng);
                }
            }
        }

        if (Math.Sqrt(bestSquared) > maxDistanceM)
        {
            return false;
        }

        snapped = best;
        return true;
    }

    /// <summary>
    /// True when <paramref name="lat"/>/<paramref name="lng"/> lies within
    /// <paramref name="maxDistanceM"/> of any segment of the polyline, measured
    /// in the same local equirectangular projection as snapping. Used to scope
    /// the connectivity rule to the network that is actually near a generated
    /// road's corridor (RoadGenerationService).
    /// </summary>
    public static bool IsNearLine(
        double lat, double lng, IReadOnlyList<(double Lat, double Lng)> line, double maxDistanceM)
    {
        if (line.Count < 2)
        {
            return false;
        }

        var cosLat = Math.Cos(lat * Math.PI / 180.0);
        var maxSquared = maxDistanceM * maxDistanceM;
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var a = line[i];
            var b = line[i + 1];
            if (SquaredSegmentDistanceM(lat, lng, a.Lat, a.Lng, b.Lat, b.Lng, cosLat) <= maxSquared)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Shortest distance in metres between two polylines: the minimum over every
    /// pair of segments of the point-to-segment distance, measured in the same
    /// local equirectangular projection as snapping. Segments that touch, cross
    /// or overlap are distance 0 — a shared junction is adjacency, not isolation.
    /// Used by the isolation half of the short-road rule: a road is removed as
    /// TooShort only when no other road lies within
    /// <see cref="RoadRulesOptions.RoadIsolationMeters"/>.
    /// </summary>
    public static double DistanceBetweenLinesM(
        IReadOnlyList<(double Lat, double Lng)> a,
        IReadOnlyList<(double Lat, double Lng)> b)
    {
        if (a.Count < 2 || b.Count < 2)
        {
            return 0.0;
        }

        var minSquared = double.MaxValue;
        for (var i = 0; i + 1 < a.Count; i++)
        {
            var (a1Lat, a1Lng) = a[i];
            var (a2Lat, a2Lng) = a[i + 1];
            var cosLat = Math.Cos(a1Lat * Math.PI / 180.0);
            for (var j = 0; j + 1 < b.Count; j++)
            {
                var (b1Lat, b1Lng) = b[j];
                var (b2Lat, b2Lng) = b[j + 1];
                minSquared = Math.Min(minSquared, SegmentPairDistanceSquaredM(
                    a1Lat, a1Lng, a2Lat, a2Lng, b1Lat, b1Lng, b2Lat, b2Lng, cosLat));
            }
        }

        return Math.Sqrt(minSquared);
    }

    /// <summary>
    /// Splits <paramref name="line"/> at every point where it properly crosses a
    /// polyline of <paramref name="network"/> (segment interiors intersecting),
    /// so no generated road pierces an existing — or earlier-accepted — road:
    /// each crossing becomes a topology node shared by the resulting pieces.
    /// Endpoint touches and T-junctions do not split (endpoint snapping handles
    /// those), and pieces shorter than <see cref="SplitSliverEpsilonM"/> are
    /// dropped. When nothing crosses the line, the line itself is returned.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> SplitLineAtCrossings(
        IReadOnlyList<(double Lat, double Lng)> line,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network)
    {
        if (line.Count < 2 || network.Count == 0)
        {
            return [line];
        }

        // Project into local equirectangular metres anchored at the first vertex
        // (the constant cos(lat) is accurate over a few-km corridor).
        var cosLat = Math.Cos(line[0].Lat * Math.PI / 180.0);
        var points = new (double X, double Y)[line.Count];
        var segmentLengths = new double[line.Count - 1];
        var cumulative = new double[line.Count - 1];
        var totalLength = 0.0;
        for (var i = 0; i < line.Count; i++)
        {
            points[i] = (
                (line[i].Lng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat,
                (line[i].Lat - line[0].Lat) * EarthMetersPerDegreeLat);
            if (i > 0)
            {
                segmentLengths[i - 1] = HypotMetres(points[i - 1], points[i]);
                cumulative[i - 1] = totalLength;
                totalLength += segmentLengths[i - 1];
            }
        }

        // Collect crossing distances along the line. Crossings at the very ends
        // (a T-junction the snap step merges) are not split points.
        var cuts = new List<double>();
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var (aX, aY) = points[i];
            var (bX, bY) = points[i + 1];
            foreach (var road in network)
            {
                for (var j = 0; j + 1 < road.Count; j++)
                {
                    var (cLat, cLng) = road[j];
                    var (dLat, dLng) = road[j + 1];
                    var cX = (cLng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat;
                    var cY = (cLat - line[0].Lat) * EarthMetersPerDegreeLat;
                    var dX = (dLng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat;
                    var dY = (dLat - line[0].Lat) * EarthMetersPerDegreeLat;

                    if (!TrySegmentInteriorIntersection(aX, aY, bX, bY, cX, cY, dX, dY, out var t))
                    {
                        continue;
                    }

                    var crossDistance = cumulative[i] + t * segmentLengths[i];
                    if (crossDistance > 0.5 && crossDistance < totalLength - 0.5)
                    {
                        cuts.Add(crossDistance);
                    }
                }
            }
        }

        if (cuts.Count == 0)
        {
            return [line];
        }

        cuts.Sort();
        var unique = new List<double>(cuts.Count);
        foreach (var cut in cuts)
        {
            if (unique.Count == 0 || cut - unique[^1] >= 1.0)
            {
                unique.Add(cut);
            }
        }

        // Walk the line, closing each piece at its crossing node.
        var pieces = new List<IReadOnlyList<(double Lat, double Lng)>>();
        var current = new List<(double Lat, double Lng)> { line[0] };
        var distance = 0.0;
        var cutIndex = 0;
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var (aLat, aLng) = line[i];
            var (bLat, bLng) = line[i + 1];
            var segmentStart = distance;
            var segmentEnd = distance + segmentLengths[i];

            while (cutIndex < unique.Count)
            {
                var cut = unique[cutIndex];
                if (cut > segmentEnd + 1.0)
                {
                    break;
                }

                var clamped = Math.Clamp(cut, segmentStart, segmentEnd);
                var t = segmentLengths[i] == 0.0 ? 0.0 : (clamped - segmentStart) / segmentLengths[i];
                var (lat, lng) = (aLat + t * (bLat - aLat), aLng + t * (bLng - aLng));
                current.Add((lat, lng));
                AddPiece(pieces, current);
                current = new List<(double Lat, double Lng)> { (lat, lng) };
                cutIndex++;
            }

            current.Add((bLat, bLng));
            distance = segmentEnd;
        }

        AddPiece(pieces, current);
        return pieces.Count == 0 ? [line] : pieces;
    }

    /// <summary>
    /// Extends a ray starting at (<paramref name="lat"/>, <paramref name="lng"/>)
    /// in the direction of the road's final bearing (toward
    /// <paramref name="prevLat"/>/<paramref name="prevLng"/>) and returns the
    /// closest network segment it hits within <paramref name="maxDistanceM"/>.
    /// The first hit becomes a T-junction, so the extension never pierces a road
    /// it passes over — welding stops at the nearest crossing. Returns false when
    /// the ray touches nothing within range.
    /// </summary>
    public static bool TryExtendToNetwork(
        double lat, double lng, double prevLat, double prevLng,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network,
        double maxDistanceM,
        out (double Lat, double Lng) hit)
    {
        hit = (lat, lng);
        var cosLat = Math.Cos(lat * Math.PI / 180.0);

        // Direction of travel as a unit vector in a local east/north metre
        // frame (x = east, y = north), so every point in this method uses the
        // same axes and the ray/segment intersection below is exact even for
        // perfectly axis-aligned roads.
        var north = (lat - prevLat) * EarthMetersPerDegreeLat;
        var east = (lng - prevLng) * cosLat * EarthMetersPerDegreeLat;
        var length = Math.Sqrt(north * north + east * east);
        if (length < 1e-9)
        {
            return false;
        }

        var dNorth = north / length;
        var dEast = east / length;

        var bestSquared = double.MaxValue;
        (double Lat, double Lng) best = (lat, lng);
        var hitFound = false;

        foreach (var road in network)
        {
            for (var i = 0; i + 1 < road.Count; i++)
            {
                var aLat = road[i].Lat;
                var aLng = road[i].Lng;
                var bLat = road[i + 1].Lat;
                var bLng = road[i + 1].Lng;

                // Segment endpoints in the ray's local east/north frame.
                var aEast = (aLng - lng) * cosLat * EarthMetersPerDegreeLat;
                var aNorth = (aLat - lat) * EarthMetersPerDegreeLat;
                var sEast = (bLng - aLng) * cosLat * EarthMetersPerDegreeLat;
                var sNorth = (bLat - aLat) * EarthMetersPerDegreeLat;

                // Ray O + t*d ; segment a + u*s, solved component-wise:
                //   t*dEast - u*sEast = aEast
                //   t*dNorth - u*sNorth = aNorth
                var det = dEast * (-sNorth) - (-sEast) * dNorth;
                if (Math.Abs(det) < 1e-9)
                {
                    continue;
                }

                var t = (aEast * (-sNorth) - (-sEast) * aNorth) / det;
                var u = (dEast * aNorth - dNorth * aEast) / det;

                // u in [0,1] and t strictly forward of the origin. A hit exactly
                // at the origin would be a degenerate junction — skip it.
                const double forwardEps = 0.25;
                if (u < -1e-9 || u > 1.0 + 1e-9 || t < forwardEps)
                {
                    continue;
                }

                var dSq = t * t;
                if (dSq < bestSquared)
                {
                    bestSquared = dSq;
                    var hitLat = lat + t * (dNorth / EarthMetersPerDegreeLat);
                    var hitLng = lng + t * (dEast / (cosLat * EarthMetersPerDegreeLat));
                    best = (hitLat, hitLng);
                    hitFound = true;
                }
            }
        }

        if (!hitFound || Math.Sqrt(bestSquared) > maxDistanceM)
        {
            return false;
        }

        hit = best;
        return true;
    }

    /// <summary>
    /// True when <paramref name="line"/> has a proper interior-interior crossing
    /// with any segment of any road in <paramref name="network"/> (the same test
    /// <see cref="SplitLineAtCrossings"/> uses). The weld pass rejects a snapped
    /// endpoint when moving it would make the road pierce another road instead of
    /// merely touching it.
    /// </summary>
    public static bool ProperlyCrossesAnyNetworkSegment(
        IReadOnlyList<(double Lat, double Lng)> line,
        IReadOnlyList<IReadOnlyList<(double Lat, double Lng)>> network)
    {
        if (line.Count < 2 || network.Count == 0)
        {
            return false;
        }

        var cosLat = Math.Cos(line[0].Lat * Math.PI / 180.0);
        for (var i = 0; i + 1 < line.Count; i++)
        {
            var aLat = line[i].Lat;
            var aLng = line[i].Lng;
            var bLat = line[i + 1].Lat;
            var bLng = line[i + 1].Lng;

            // Near-degenerate segment (sub-epsilon length): projection can flicker
            // a crossing when a road hugs its own endpoint; skip.
            if (Math.Abs((bLat - aLat) * EarthMetersPerDegreeLat) < 1e-6
                && Math.Abs((bLng - aLng) * cosLat * EarthMetersPerDegreeLat) < 1e-6)
            {
                continue;
            }

            foreach (var road in network)
            {
                for (var j = 0; j + 1 < road.Count; j++)
                {
                    var cLat = road[j].Lat;
                    var cLng = road[j].Lng;
                    var dLat = road[j + 1].Lat;
                    var dLng = road[j + 1].Lng;

                    var cX = (cLng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat;
                    var cY = (cLat - line[0].Lat) * EarthMetersPerDegreeLat;
                    var dX = (dLng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat;
                    var dY = (dLat - line[0].Lat) * EarthMetersPerDegreeLat;

                    var aX = (aLng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat;
                    var aY = (aLat - line[0].Lat) * EarthMetersPerDegreeLat;
                    var bX = (bLng - line[0].Lng) * cosLat * EarthMetersPerDegreeLat;
                    var bY = (bLat - line[0].Lat) * EarthMetersPerDegreeLat;

                    if (TrySegmentInteriorIntersection(aX, aY, bX, bY, cX, cY, dX, dY, out _))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static void AddPiece(
        List<IReadOnlyList<(double Lat, double Lng)>> pieces,
        IReadOnlyList<(double Lat, double Lng)> piece)
    {
        if (PieceLengthM(piece) >= SplitSliverEpsilonM)
        {
            pieces.Add(piece);
        }
    }

    private static double PieceLengthM(IReadOnlyList<(double Lat, double Lng)> piece)
    {
        var total = 0.0;
        for (var i = 1; i < piece.Count; i++)
        {
            total += DraftGeometry.HaversineM(
                piece[i - 1].Lng, piece[i - 1].Lat, piece[i].Lng, piece[i].Lat);
        }

        return total;
    }

    private static double HypotMetres((double X, double Y) a, (double X, double Y) b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// True when segments a→b and c→d cross with both intersection parameters in
    /// the open unit interval (interior-in-interior). Collinear overlaps and
    /// endpoint touches (T-junctions) are not crossings. <paramref name="t"/> is
    /// the intersection parameter along a→b.
    /// </summary>
    private static bool TrySegmentInteriorIntersection(
        double aX, double aY, double bX, double bY,
        double cX, double cY, double dX, double dY,
        out double t)
    {
        t = 0.0;
        var rX = bX - aX;
        var rY = bY - aY;
        var sX = dX - cX;
        var sY = dY - cY;
        var cross = rX * sY - rY * sX;
        if (Math.Abs(cross) < 1e-9)
        {
            return false;
        }

        var qpX = cX - aX;
        var qpY = cY - aY;
        t = (qpX * sY - qpY * sX) / cross;
        var u = (qpX * rY - qpY * rX) / cross;

        const double edge = 1e-6;
        return t > edge && t < 1.0 - edge && u > edge && u < 1.0 - edge;
    }

    private static double SquaredSegmentDistanceM(
        double lat, double lng, double aLat, double aLng, double bLat, double bLng, double cosLat)
    {
        var ax = (aLng - lng) * cosLat * EarthMetersPerDegreeLat;
        var ay = (aLat - lat) * EarthMetersPerDegreeLat;
        var bx = (bLng - lng) * cosLat * EarthMetersPerDegreeLat;
        var by = (bLat - lat) * EarthMetersPerDegreeLat;

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

    /// <summary>
    /// Squared distance between two segments, projected into a local metre plane
    /// anchored at <paramref name="a1Lat"/>/<paramref name="a1Lng"/>. Segments
    /// that intersect or touch anywhere are distance 0; otherwise the distance
    /// is the minimum of the four endpoint-to-segment distances.
    /// </summary>
    private static double SegmentPairDistanceSquaredM(
        double a1Lat, double a1Lng, double a2Lat, double a2Lng,
        double b1Lat, double b1Lng, double b2Lat, double b2Lng, double cosLat)
    {
        var a2X = (a2Lng - a1Lng) * cosLat * EarthMetersPerDegreeLat;
        var a2Y = (a2Lat - a1Lat) * EarthMetersPerDegreeLat;
        var b1X = (b1Lng - a1Lng) * cosLat * EarthMetersPerDegreeLat;
        var b1Y = (b1Lat - a1Lat) * EarthMetersPerDegreeLat;
        var b2X = (b2Lng - a1Lng) * cosLat * EarthMetersPerDegreeLat;
        var b2Y = (b2Lat - a1Lat) * EarthMetersPerDegreeLat;

        if (SegmentsShareLine(0.0, 0.0, a2X, a2Y, b1X, b1Y, b2X, b2Y))
        {
            return 0.0;
        }

        return Math.Min(
            PointToSegmentSquaredM(0.0, 0.0, b1X, b1Y, b2X, b2Y),
            Math.Min(
                PointToSegmentSquaredM(a2X, a2Y, b1X, b1Y, b2X, b2Y),
                Math.Min(
                    PointToSegmentSquaredM(b1X, b1Y, 0.0, 0.0, a2X, a2Y),
                    PointToSegmentSquaredM(b2X, b2Y, 0.0, 0.0, a2X, a2Y))));
    }

    /// <summary>
    /// True when the two segments share any point (proper crossing, endpoint
    /// touch, or collinear overlap) in the local metre plane. The orientation
    /// (cross-product) test with collinear/touch checks.
    /// </summary>
    private static bool SegmentsShareLine(
        double aX, double aY, double bX, double bY,
        double cX, double cY, double dX, double dY)
    {
        var d1 = Cross(cX, cY, dX, dY, aX, aY);
        var d2 = Cross(cX, cY, dX, dY, bX, bY);
        var d3 = Cross(aX, aY, bX, bY, cX, cY);
        var d4 = Cross(aX, aY, bX, bY, dX, dY);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        return (d1 == 0 && OnSegment(cX, cY, dX, dY, aX, aY))
            || (d2 == 0 && OnSegment(cX, cY, dX, dY, bX, bY))
            || (d3 == 0 && OnSegment(aX, aY, bX, bY, cX, cY))
            || (d4 == 0 && OnSegment(aX, aY, bX, bY, dX, dY));
    }

    private static double Cross(double pX, double pY, double qX, double qY, double rX, double rY)
        => (qX - pX) * (rY - pY) - (qY - pY) * (rX - pX);

    private static bool OnSegment(
        double pX, double pY, double qX, double qY, double rX, double rY)
        => rX >= Math.Min(pX, qX) && rX <= Math.Max(pX, qX)
            && rY >= Math.Min(pY, qY) && rY <= Math.Max(pY, qY);

    private static double PointToSegmentSquaredM(
        double pX, double pY, double aX, double aY, double bX, double bY)
    {
        var dx = bX - aX;
        var dy = bY - aY;
        var lenSq = dx * dx + dy * dy;
        var t = lenSq == 0.0
            ? 0.0
            : Math.Clamp(-((aX - pX) * dx + (aY - pY) * dy) / lenSq, 0.0, 1.0);
        var qX = aX + t * dx - pX;
        var qY = aY + t * dy - pY;
        return qX * qX + qY * qY;
    }
}
