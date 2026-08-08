using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using NarsApi.DTOs;

namespace NarsApi.Infrastructure;

/// <summary>
/// Pure geometry math utilities — no state, no DI, independently testable.
/// </summary>
public static class GeometryHelper
{
    private const double DegToRad = Math.PI / 180.0;

    /// <summary>
    /// Finds the nearest road segment to a marker point using cosine-corrected Euclidean distance.
    /// Returns the index of the nearest segment in <paramref name="roadCoords"/>.
    /// </summary>
    public static int FindNearestSegmentIndex(
        double markerLat, double markerLng, IReadOnlyList<(double Lat, double Lng)> roadCoords)
    {
        var cosLat = Math.Cos(markerLat * DegToRad);
        var minDist = double.MaxValue;
        var nearestIdx = 0;

        for (var i = 0; i < roadCoords.Count - 1; i++)
        {
            var midLat = (roadCoords[i].Lat + roadCoords[i + 1].Lat) / 2;
            var midLng = (roadCoords[i].Lng + roadCoords[i + 1].Lng) / 2;
            var dLat = markerLat - midLat;
            var dLng = (markerLng - midLng) * cosLat;
            var d = Math.Sqrt(dLat * dLat + dLng * dLng);
            if (d < minDist)
            {
                minDist = d;
                nearestIdx = i;
            }
        }

        return nearestIdx;
    }

    /// <summary>
    /// Determines which side of a directed segment the point lies on using the cross product.
    /// Returns "left" if cross product ≥ 0, "right" otherwise.
    /// </summary>
    public static string DetermineSide(
        double markerLat, double markerLng,
        double segStartLat, double segStartLng,
        double segEndLat, double segEndLng)
    {
        var cross = (segEndLng - segStartLng) * (markerLat - segStartLat)
                  - (segEndLat - segStartLat) * (markerLng - segStartLng);
        return cross >= 0 ? "left" : "right";
    }

    /// <summary>
    /// Computes the turn angle (in degrees) at the middle point of three consecutive coordinates.
    /// Returns 0 if segments are degenerate (length ≈ 0).
    /// </summary>
    public static double ComputeTurnAngle(
        double lat1, double lng1,
        double lat2, double lng2,
        double lat3, double lng3)
    {
        double v1x = lng2 - lng1, v1y = lat2 - lat1;
        double v2x = lng3 - lng2, v2y = lat3 - lat2;

        var len1 = Math.Sqrt(v1x * v1x + v1y * v1y);
        var len2 = Math.Sqrt(v2x * v2x + v2y * v2y);

        if (len1 < 1e-10 || len2 < 1e-10)
        {
            return 0;
        }

        var dot = (v1x * v2x + v1y * v2y) / (len1 * len2);
        return Math.Acos(Math.Clamp(dot, -1.0, 1.0)) * (180.0 / Math.PI);
    }

    /// <summary>
    /// Suggests the next available entrance number (odd for left, even for right).
    /// Parity is preserved even past 10,000 so an exhausted series never collides
    /// with the opposite parity (e.g. 10000 must not be reused by the odd series).
    /// </summary>
    public static int SuggestEntranceNumber(string side, HashSet<int> usedNumbers)
    {
        var suggested = side == "left" ? 1 : 2;
        while (usedNumbers.Contains(suggested) && suggested < 100_000)
        {
            suggested += 2;
        }
        return suggested;
    }

    public static string FormatDoubleInvariant(double v) => v.ToString(CultureInfo.InvariantCulture);

    public static void AppendWktCoords(StringBuilder sb, List<CoordDto> coords)
    {
        for (var i = 0; i < coords.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(FormatDoubleInvariant(coords[i].Lng));
            sb.Append(' ');
            sb.Append(FormatDoubleInvariant(coords[i].Lat));
        }
    }

    public static string BuildLineStringWkt(List<CoordDto> coords)
    {
        var sb = new StringBuilder("LINESTRING(");
        AppendWktCoords(sb, coords);
        sb.Append(')');
        return sb.ToString();
    }

    public static string BuildPolygonWkt(List<CoordDto> coords)
    {
        if (coords.Count < 3)
        {
            throw new ArgumentException("Polygon requires at least 3 coordinates.");
        }

        var sb = new StringBuilder("POLYGON((");
        AppendWktCoords(sb, coords);
        var first = $"{FormatDoubleInvariant(coords[0].Lng)} {FormatDoubleInvariant(coords[0].Lat)}";
        var last = $"{FormatDoubleInvariant(coords[^1].Lng)} {FormatDoubleInvariant(coords[^1].Lat)}";
        if (first != last)
        {
            sb.Append(',');
            sb.Append(first);
        }
        sb.Append("))");
        return sb.ToString();
    }

    public static List<(double Lat, double Lng)> ParseRoadCoordinates(JsonNode coordsNode)
    {
        if (coordsNode is not JsonArray coordsArr || coordsArr.Count < 2)
        {
            throw new ArgumentException("Road data is missing coordinates.");
        }

        var roadCoords = new List<(double Lat, double Lng)>(coordsArr.Count);
        foreach (var c in coordsArr)
        {
            if (c is not JsonObject obj ||
                !obj.TryGetPropertyValue("lat", out var latNode) ||
                !obj.TryGetPropertyValue("lng", out var lngNode) ||
                latNode is not JsonValue latVal || !latVal.TryGetValue(out double lat) ||
                lngNode is not JsonValue lngVal || !lngVal.TryGetValue(out double lng))
            {
                throw new ArgumentException("Road coordinate entry is missing 'lat' or 'lng' or has invalid values.");
            }
            roadCoords.Add((lat, lng));
        }

        if (roadCoords.Count < 2)
        {
            throw new ArgumentException("Road has insufficient coordinates.");
        }

        return roadCoords;
    }
}
