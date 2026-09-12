using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using NarsApi.Models;

namespace NarsApi.Infrastructure;

/// <summary>
/// Converts an AI draft's GeoJSON geometry into the shape production feature
/// tables expect, and evaluates the road/building limitation rules in pure C#
/// (no PostGIS) so draft acceptance is testable against the InMemory provider.
///
/// Production features store their coordinates in the legacy
/// <c>coordinates: [{lat, lng}, ...]</c> JSONB shape (see SqlFragments
/// LineStringFromDataTemplate), NOT GeoJSON [lng, lat] pairs — <see
/// cref="ToRoadData"/> performs that inverse conversion for materialized
/// roads.
/// </summary>
public static class DraftGeometry
{
    /// <summary>
    /// Maximum distance (metres) from a building footprint to the nearest road
    /// segment for that road to be the entrance's reference/anchor road. Beyond
    /// this, the accepted building has no road in the commune to attach to, so
    /// it stays pending instead of producing an orphaned entrance.
    /// </summary>
    public const double MaxReferenceRoadDistanceM = 100.0;

    /// <summary>
    /// Sums the great-circle length (metres) of a GeoJSON LineString geometry.
    /// Returns false when the geometry is not a LineString or is malformed, so
    /// callers can reject a draft whose geometry never was a road.
    /// GeoJSON coordinate arrays may carry a trailing Z value; only the first
    /// two entries (lng, lat) are used.
    /// </summary>
    public static bool TryGetLineLengthM(string geometryGeoJson, out double lengthM)
    {
        lengthM = 0.0;
        if (!TryReadLineCoordinates(geometryGeoJson, out var coordinates))
        {
            return false;
        }

        (double Lon, double Lat)? previous = null;
        foreach (var point in coordinates)
        {
            if (!TryGetLonLat(point, out var lon, out var lat))
            {
                return false;
            }

            if (previous is var (prevLon, prevLat))
            {
                lengthM += HaversineM(prevLon, prevLat, lon, lat);
            }

            previous = (lon, lat);
        }

        return previous is not null;
    }

    /// <summary>
    /// Validates a draft edit: the geometry must be a GeoJSON object whose
    /// top-level type matches the draft's feature type (LineString for roads,
    /// Polygon for buildings) and whose coordinates parse. This keeps a road
    /// draft from silently becoming a different geometry kind on accept.
    /// </summary>
    public static bool IsGeometryKind(string geometryGeoJson, string expectedKind)
    {
        using var doc = TryParse(geometryGeoJson);
        return doc is not null
            && doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("type", out var type)
            && type.GetString() == expectedKind
            && doc.RootElement.TryGetProperty("coordinates", out _);
    }

    /// <summary>
    /// Builds the production <c>data</c> JSONB payload for a materialized road:
    /// mirrors the FeatureData object the web front-end sends for user-drawn
    /// roads (type + default layer "street" + coordinates as lat/lng objects).
    /// </summary>
    public static JsonObject ToRoadData(string geometryGeoJson)
    {
        var coordinates = new JsonArray();
        if (TryParse(geometryGeoJson) is { } doc)
        {
            using (doc)
            {
                if (doc.RootElement.TryGetProperty("coordinates", out var coords)
                    && coords.ValueKind == JsonValueKind.Array)
                {
                    foreach (var point in coords.EnumerateArray())
                    {
                        if (TryGetLonLat(point, out var lon, out var lat))
                        {
                            coordinates.Add(new JsonObject { ["lat"] = lat, ["lng"] = lon });
                        }
                    }
                }
            }
        }

        return new JsonObject
        {
            ["type"] = "road",
            ["label"] = "",
            ["roadTypeKey"] = FeatureTypes.RoadLayers.Street,
            ["coordinates"] = coordinates,
        };
    }

    /// <summary>
    /// Extracts the outer ring of a GeoJSON Polygon as (Lon, Lat) pairs. The
    /// expected geometry kind for a building draft. Returns false when the
    /// geometry is not a Polygon or its ring is malformed.
    /// </summary>
    public static bool TryGetPolygonRing(string geometryGeoJson, out IReadOnlyList<(double Lon, double Lat)> ring)
    {
        ring = [];
        using var doc = TryParse(geometryGeoJson);
        if (doc is null)
        {
            return false;
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type)
            || type.GetString() != "Polygon"
            || !root.TryGetProperty("coordinates", out var coords)
            || coords.ValueKind != JsonValueKind.Array
            || coords.GetArrayLength() < 1)
        {
            return false;
        }

        var outer = coords[0];
        if (outer.ValueKind != JsonValueKind.Array || outer.GetArrayLength() < 3)
        {
            return false;
        }

        var list = new List<(double Lon, double Lat)>(outer.GetArrayLength());
        foreach (var point in outer.EnumerateArray())
        {
            if (!TryGetLonLat(point, out var lon, out var lat))
            {
                return false;
            }

            list.Add((lon, lat));
        }

        ring = list;
        return true;
    }

    /// <summary>
    /// Mean vertex position of a polygon ring. Good enough as the anchor for
    /// side/distance decisions on the small building footprints segma produces.
    /// </summary>
    public static (double Lon, double Lat) PolygonCentroid(IReadOnlyList<(double Lon, double Lat)> ring)
    {
        if (ring.Count == 0)
        {
            return (0.0, 0.0);
        }

        double lonSum = 0.0;
        double latSum = 0.0;
        foreach (var (lon, lat) in ring)
        {
            lonSum += lon;
            latSum += lat;
        }

        return (lonSum / ring.Count, latSum / ring.Count);
    }

    /// <summary>
    /// Finds the ring vertex nearest to an anchor position (using
    /// cosine-corrected Euclidean distance, same convention as
    /// GeometryHelper.FindNearestSegmentIndex). The result is the "front point"
    /// of the building facing the anchor, returned as (Lat, Lng).
    /// </summary>
    public static (double Lat, double Lng) FindNearestRingVertex(
        double anchorLon, double anchorLat, IReadOnlyList<(double Lon, double Lat)> ring)
    {
        var cosLat = Math.Cos(anchorLat * Math.PI / 180.0);
        var minDist = double.MaxValue;
        var nearestLon = 0.0;
        var nearestLat = 0.0;

        foreach (var (lon, lat) in ring)
        {
            var dLat = anchorLat - lat;
            var dLon = (anchorLon - lon) * cosLat;
            var d = Math.Sqrt(dLat * dLat + dLon * dLon);
            if (d < minDist)
            {
                minDist = d;
                nearestLon = lon;
                nearestLat = lat;
            }
        }

        return (nearestLat, nearestLon);
    }

    /// <summary>
    /// Builds the production <c>data</c> JSONB payload for the house entrance
    /// created when an AI building draft is accepted. Mirrors the web front-end's
    /// HouseEntranceFeatureData (type "houseEntrances" + entranceTypeKey +
    /// road/road-side/number fields + a lat/lng point, which the loader renders
    /// as a marker) and carries the label as the number, matching
    /// NumberEntrancesService.SetEntranceNumber. The original AI footprint is
    /// preserved under <c>buildFootprint</c> so the accepted building is not
    /// lost — an extra key the web ignores.
    /// </summary>
    public static JsonObject ToHouseEntranceData(
        Guid? roadId, string roadLabel, string? side, int? entranceNumber,
        double lat, double lng, string footprintGeoJson)
    {
        var data = new JsonObject
        {
            ["type"] = "houseEntrances",
            ["label"] = entranceNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            ["entranceTypeKey"] = FeatureTypes.HouseEntranceLayers.Main,
            ["lat"] = lat,
            ["lng"] = lng,
            ["coordinates"] = new JsonArray
            {
                new JsonObject { ["lat"] = lat, ["lng"] = lng },
            },
        };

        if (roadId is { } rid)
        {
            data["roadDbId"] = rid.ToString();
            data["roadLabel"] = roadLabel;
            data["side"] = side;
            data["entranceNumber"] = entranceNumber;
        }

        if (TryParse(footprintGeoJson) is { } doc)
        {
            using (doc)
            {
                data["buildFootprint"] = JsonNode.Parse(doc.RootElement.GetRawText());
            }
        }

        return data;
    }

    private static bool TryReadLineCoordinates(string geometryGeoJson, out List<JsonElement> coordinates)
    {
        coordinates = [];
        using var doc = TryParse(geometryGeoJson);
        if (doc is null)
        {
            return false;
        }

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("type", out var type)
            || type.GetString() != "LineString"
            || !root.TryGetProperty("coordinates", out var coords)
            || coords.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (var point in coords.EnumerateArray())
        {
            coordinates.Add(point.Clone());
        }

        return true;
    }

    private static bool TryGetLonLat(JsonElement point, out double lon, out double lat)
    {
        lon = 0.0;
        lat = 0.0;
        if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() < 2)
        {
            return false;
        }

        lon = point[0].GetDouble();
        lat = point[1].GetDouble();
        return true;
    }

    private static JsonDocument? TryParse(string geometryGeoJson)
    {
        try
        {
            return JsonDocument.Parse(geometryGeoJson);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Great-circle distance between two lon/lat positions in metres.</summary>
    public static double HaversineM(double lon1, double lat1, double lon2, double lat2)
    {
        const double earthRadiusM = 6_371_000.0;

        static double ToRadians(double deg) => deg * Math.PI / 180.0;

        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0)
                + Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2))
                * Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);
        return 2.0 * earthRadiusM * Math.Asin(Math.Sqrt(a));
    }
}