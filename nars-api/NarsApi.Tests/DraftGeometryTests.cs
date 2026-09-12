using NarsApi.Infrastructure;
using System.Text.Json.Nodes;
using Xunit;

namespace NarsApi.Tests;

public class DraftGeometryTests
{
    [Fact]
    public void TryGetLineLengthM_LongerLine_ReturnsGreaterLength()
    {
        const string shortLine = """{"type":"LineString","coordinates":[[36.7200,2.9600],[36.7201,2.9601]]}""";
        const string longLine = """{"type":"LineString","coordinates":[[36.70,2.95],[36.73,2.98]]}""";

        Assert.True(DraftGeometry.TryGetLineLengthM(shortLine, out var shortM));
        Assert.True(DraftGeometry.TryGetLineLengthM(longLine, out var longM));
        Assert.True(shortM < longM);
        Assert.True(longM > 2000);
    }

    [Fact]
    public void TryGetLineLengthM_InvalidJson_ReturnsFalse()
    {
        Assert.False(DraftGeometry.TryGetLineLengthM("not json", out _));
    }

    [Fact]
    public void TryGetLineLengthM_WrongType_ReturnsFalse()
    {
        Assert.False(DraftGeometry.TryGetLineLengthM("""{"type":"Point","coordinates":[36.7,2.9]}""", out _));
    }

    [Theory]
    [InlineData("""{"type":"LineString","coordinates":[[36.7,2.9],[36.8,3.0]]}""", "LineString", true)]
    [InlineData("""{"type":"LineString","coordinates":[[36.7,2.9],[36.8,3.0]]}""", "Polygon", false)]
    [InlineData("""{"type":"Polygon","coordinates":[[[36.7,2.9],[36.8,2.9],[36.8,3.0],[36.7,2.9]]]}""", "Polygon", true)]
    [InlineData("""{"type":"Point","coordinates":[36.7,2.9]}""", "Polygon", false)]
    [InlineData("not json", "LineString", false)]
    public void IsGeometryKind_MatchesExpectedKind(string geometry, string expectedKind, bool expected)
        => Assert.Equal(expected, DraftGeometry.IsGeometryKind(geometry, expectedKind));

    [Fact]
    public void ToRoadData_BuildsLatLngObjectCoordinates()
    {
        const string line = """{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}""";

        JsonObject data = DraftGeometry.ToRoadData(line);

        Assert.Equal("road", data["type"]!.GetValue<string>());
        Assert.Equal("street", data["roadTypeKey"]!.GetValue<string>());
        Assert.Equal("", data["label"]!.GetValue<string>());
        var coords = data["coordinates"]!.AsArray();
        Assert.Equal(2, coords.Count);
        Assert.Equal(2.96, coords[0]!["lat"]!.GetValue<double>());
        Assert.Equal(36.72, coords[0]!["lng"]!.GetValue<double>());
    }

    [Fact]
    public void ToRoadData_InvalidGeometry_ProducesEmptyCoordinates()
    {
        JsonObject data = DraftGeometry.ToRoadData("not json");

        Assert.Empty(data["coordinates"]!.AsArray());
    }

    [Fact]
    public void TryGetPolygonRing_ExtractsOuterRing()
    {
        const string polygon = """{"type":"Polygon","coordinates":[[[36.72,2.96],[36.73,2.96],[36.73,2.97],[36.72,2.96]]]}""";

        Assert.True(DraftGeometry.TryGetPolygonRing(polygon, out var ring));

        Assert.Equal(4, ring.Count);
        Assert.Equal((36.72, 2.96), ring[0]);
        Assert.Equal((36.73, 2.97), ring[2]);
    }

    [Fact]
    public void TryGetPolygonRing_ClosedRing_IncludesLastVertex()
    {
        const string polygon = """{"type":"Polygon","coordinates":[[[36.72,2.96],[36.73,2.97],[36.72,2.96]]]}""";

        Assert.True(DraftGeometry.TryGetPolygonRing(polygon, out var ring));
        Assert.Equal(3, ring.Count);
    }

    [Theory]
    [InlineData("not json", false)]
    [InlineData("""{"type":"LineString","coordinates":[[36.72,2.96],[36.73,2.97]]}""", false)]
    [InlineData("""{"type":"Polygon","coordinates":[]}""", false)]
    public void TryGetPolygonRing_InvalidGeometry_ReturnsFalse(string geometry, bool expected)
        => Assert.Equal(expected, DraftGeometry.TryGetPolygonRing(geometry, out _));

    [Fact]
    public void PolygonCentroid_AveragesVertices()
    {
        var ring = new List<(double Lon, double Lat)> { (36.70, 2.90), (36.72, 2.90), (36.72, 2.94), (36.70, 2.94) };

        var centroid = DraftGeometry.PolygonCentroid(ring);

        Assert.Equal((36.71, 2.92), centroid);
    }

    [Fact]
    public void FindNearestRingVertex_ReturnsClosestVertex()
    {
        var ring = new List<(double Lon, double Lat)> { (36.70, 2.90), (36.72, 2.90), (36.72, 2.94), (36.70, 2.94) };

        var nearest = DraftGeometry.FindNearestRingVertex(anchorLon: 36.7195, anchorLat: 2.9001, ring);

        Assert.Equal((2.90, 36.72), nearest);
    }

    [Fact]
    public void ToHouseEntranceData_Numbered_BuildsFeatureShape()
    {
        const string footprint = """{"type":"Polygon","coordinates":[[[36.72,2.96],[36.721,2.96],[36.721,2.961],[36.72,2.96]]]}""";
        var roadId = Guid.NewGuid();

        JsonObject data = DraftGeometry.ToHouseEntranceData(roadId, "Rue 1", "right", 4, 2.9605, 36.7205, footprint);

        Assert.Equal("houseEntrances", data["type"]!.GetValue<string>());
        Assert.Equal("4", data["label"]!.GetValue<string>());
        Assert.Equal("main_entrance", data["entranceTypeKey"]!.GetValue<string>());
        Assert.Equal(roadId.ToString(), data["roadDbId"]!.GetValue<string>());
        Assert.Equal("right", data["side"]!.GetValue<string>());
        Assert.Equal(4, data["entranceNumber"]!.GetValue<int>());
        Assert.Equal(2.9605, data["lat"]!.GetValue<double>());
        Assert.Equal(36.7205, data["lng"]!.GetValue<double>());
        var entranceCoords = data["coordinates"]!.AsArray();
        Assert.Single(entranceCoords);
        Assert.NotNull(data["buildFootprint"]);
        string? footprintType = data["buildFootprint"]!["type"]?.GetValue<string>();
        Assert.Equal("Polygon", footprintType);
    }

    [Fact]
    public void ToHouseEntranceData_NoRoad_OmitsNumberingFields()
    {
        JsonObject data = DraftGeometry.ToHouseEntranceData(null, "", null, null, 2.9605, 36.7205, "{}");

        Assert.Equal("houseEntrances", data["type"]!.GetValue<string>());
        Assert.Equal("", data["label"]!.GetValue<string>());
        Assert.False(data.ContainsKey("roadDbId"));
        Assert.False(data.ContainsKey("entranceNumber"));
        Assert.False(data.ContainsKey("side"));
    }
}
