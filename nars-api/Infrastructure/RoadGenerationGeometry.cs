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
}
