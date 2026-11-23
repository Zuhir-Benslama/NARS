using NarsApi.DTOs;
using NarsApi.Infrastructure;
using Xunit;

namespace NarsApi.Tests;

public class GeometryHelperTests
{
    [Fact]
    public void FindNearestSegmentIndex_ReturnsCorrectIndex()
    {
        var coords = new List<(double Lat, double Lng)>
        {
            (35.0, 5.0),
            (35.0, 6.0),
            (35.0, 7.0),
            (35.0, 8.0),
        };

        // Marker near midpoint of segment 1 (between points 1 and 2)
        var idx = GeometryHelper.FindNearestSegmentIndex(35.0, 6.3, coords);

        Assert.Equal(1, idx);
    }

    [Fact]
    public void FindNearestSegmentIndex_SingleSegment_ReturnsZero()
    {
        var coords = new List<(double Lat, double Lng)>
        {
            (0.0, 0.0),
            (0.0, 1.0),
        };

        Assert.Equal(0, GeometryHelper.FindNearestSegmentIndex(0.0, 0.5, coords));
    }

    [Fact]
    public void FindNearestSegmentIndex_PointAtStart_ReturnsZero()
    {
        var coords = new List<(double Lat, double Lng)>
        {
            (10.0, 10.0),
            (10.0, 20.0),
            (10.0, 30.0),
        };

        // Marker directly on point 0
        var idx = GeometryHelper.FindNearestSegmentIndex(10.0, 10.0, coords);
        Assert.Equal(0, idx);
    }

    [Fact]
    public void DetermineSide_NorthOfSegment_ReturnsLeft()
    {
        // Moving east (0,0)->(0,1), point is north (above) -> left
        var side = GeometryHelper.DetermineSide(1.0, 0.5, 0.0, 0.0, 0.0, 1.0);

        Assert.Equal("left", side);
    }

    [Fact]
    public void DetermineSide_SouthOfSegment_ReturnsRight()
    {
        // Moving east (0,0)->(0,1), point is south (below) -> right
        var side = GeometryHelper.DetermineSide(-1.0, 0.5, 0.0, 0.0, 0.0, 1.0);

        Assert.Equal("right", side);
    }

    [Fact]
    public void DetermineSideOnPolyline_StraightRoad_MatchesSegmentSide()
    {
        var road = new List<(double Lat, double Lng)> { (0.0, 0.0), (0.0, 1.0), (0.0, 2.0) };

        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(0.1, 1.5, road));
        Assert.Equal("right", GeometryHelper.DetermineSideOnPolyline(-0.1, 1.5, road));
    }

    [Fact]
    public void DetermineSideOnPolyline_InsideOfLeftBend_IsLeft()
    {
        // East then north (left turn). A point just inside the bend (north-west of the
        // corner) is on the left of the road.
        var road = new List<(double Lat, double Lng)> { (0.0, 0.0), (0.0, 1.0), (1.0, 1.0) };

        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(0.05, 0.95, road));
    }

    [Fact]
    public void DetermineSideOnPolyline_OutsideOfLeftBend_IsRight()
    {
        // Past the corner on the outer (south-east) side: one stable "right" regardless
        // of which adjoining segment is picked.
        var road = new List<(double Lat, double Lng)> { (0.0, 0.0), (0.0, 1.0), (1.0, 1.0) };

        Assert.Equal("right", GeometryHelper.DetermineSideOnPolyline(-0.05, 1.05, road));
        Assert.Equal("right", GeometryHelper.DetermineSideOnPolyline(-0.05, 0.95, road));
        Assert.Equal("right", GeometryHelper.DetermineSideOnPolyline(0.05, 1.05, road));
    }

    [Fact]
    public void DetermineSideOnPolyline_PointBeyondShortSegmentEnd_UsesRealClosestSegment()
    {
        // Long first segment, short second. Point is near the second segment but its
        // projection lies past the first segment's end; the midpoint heuristic would
        // choose segment 0 and misjudge the side.
        var road = new List<(double Lat, double Lng)> { (0.0, 0.0), (0.0, 10.0), (0.2, 10.0) };

        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(0.1, 9.9, road));
    }

    [Fact]
    public void DetermineSideOnPolyline_FewerThanTwoCoords_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            GeometryHelper.DetermineSideOnPolyline(0, 0, new List<(double Lat, double Lng)> { (0, 0) }));
    }

    [Fact]
    public void DetermineSide_OnLine_ReturnsLeft()
    {
        // Cross product exactly zero -> left
        var side = GeometryHelper.DetermineSide(0.0, 0.5, 0.0, 0.0, 0.0, 1.0);

        Assert.Equal("left", side);
    }

    // Both side functions must share one convention (geographic left of the
    // direction of travel) for every heading; a sign mismatch between them is what
    // previously made left/right flip depending on which code path was used.
    [Theory]
    [InlineData(0.0, 0.0, 0.0, 1.0, 0.1, 0.5, "left")]   // heading east, marker north
    [InlineData(0.0, 0.0, 0.0, 1.0, -0.1, 0.5, "right")]  // heading east, marker south
    [InlineData(0.0, 0.0, 1.0, 0.0, 0.5, -0.1, "left")]   // heading north, marker west
    [InlineData(0.0, 0.0, 1.0, 0.0, 0.5, 0.1, "right")]   // heading north, marker east
    [InlineData(0.0, 1.0, 0.0, 0.0, -0.1, 0.5, "left")]   // heading west, marker south
    [InlineData(0.0, 1.0, 0.0, 0.0, 0.1, 0.5, "right")]   // heading west, marker north
    [InlineData(1.0, 0.0, 0.0, 0.0, 0.5, 0.1, "left")]    // heading south, marker east
    [InlineData(1.0, 0.0, 0.0, 0.0, 0.5, -0.1, "right")]  // heading south, marker west
    public void DetermineSide_And_DetermineSideOnPolyline_AgreeOnStraightSegments(
        double startLat, double startLng, double endLat, double endLng,
        double markerLat, double markerLng, string expected)
    {
        var road = new List<(double Lat, double Lng)> { (startLat, startLng), (endLat, endLng) };

        Assert.Equal(expected, GeometryHelper.DetermineSide(markerLat, markerLng, startLat, startLng, endLat, endLng));
        Assert.Equal(expected, GeometryHelper.DetermineSideOnPolyline(markerLat, markerLng, road));
    }

    [Fact]
    public void DetermineSideOnPolyline_ShortLegBesideLongLeg_UsesTrueClosestSegment()
    {
        // 2 degrees east, then a short 0.1 degree leg north. The marker sits just south of
        // the long leg (right of travel). It is nearer the midpoint of the short northbound
        // leg than of the long leg, so the nearest-midpoint heuristic would pick that leg
        // and report "left"; the true closest point is on the long leg: "right".
        var road = new List<(double Lat, double Lng)> { (0.0, 0.0), (0.0, 2.0), (0.1, 2.0) };

        Assert.Equal("right", GeometryHelper.DetermineSideOnPolyline(-0.04, 1.55, road));
    }

    [Fact]
    public void DetermineSideOnPolyline_FaithRoadBend_DoesNotMisclassifyCornerEntrances()
    {
        // Regression from the real "Faith" road: its bend makes the nearest
        // midpoint belong to segment 0 even for entrances that sit ~6 m on the
        // left of segment 1. The whole-polyline method judges them by the true
        // closest point, while entrances on the other side stay "right".
        var faith = new List<(double Lat, double Lng)>
        {
            (36.00989774319092, 7.421284662059691),
            (36.009630858381286, 7.421949866808546),
            (36.00763442086685, 7.423032007407272),
        };

        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(36.009574647202214, 7.422057531298776, faith));
        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(36.009485689895186, 7.422095082225127, faith));
        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(36.00941409003913, 7.422132633151449, faith));
        Assert.Equal("left", GeometryHelper.DetermineSideOnPolyline(36.00932079315834, 7.42218627733223, faith));
        Assert.Equal("right", GeometryHelper.DetermineSideOnPolyline(36.009546441237774, 7.421907327594312, faith));
    }

    [Fact]
    public void ComputeTurnAngle_StraightLine_ReturnsZero()
    {
        var angle = GeometryHelper.ComputeTurnAngle(
            0.0, 0.0,
            0.0, 1.0,
            0.0, 2.0);

        Assert.Equal(0.0, angle, 6);
    }

    [Fact]
    public void ComputeTurnAngle_NinetyDegrees()
    {
        // L-shape: east then north -> 90° turn
        var angle = GeometryHelper.ComputeTurnAngle(
            0.0, 0.0,
            0.0, 1.0,
            1.0, 1.0);

        Assert.InRange(angle, 89.0, 91.0);
    }

    [Fact]
    public void ComputeTurnAngle_DegenerateSegment_ReturnsZero()
    {
        // Zero-length first segment
        var angle = GeometryHelper.ComputeTurnAngle(
            0.0, 0.0,
            0.0, 0.0,
            1.0, 1.0);

        Assert.Equal(0.0, angle, 6);
    }

    [Fact]
    public void SuggestEntranceNumber_Left_OddFromOne()
    {
        var used = new HashSet<int> { 2, 4 };
        var num = GeometryHelper.SuggestEntranceNumber("left", used);

        Assert.Equal(1, num);
    }

    [Fact]
    public void SuggestEntranceNumber_Right_EvenFromTwo()
    {
        var used = new HashSet<int> { 1, 3 };
        var num = GeometryHelper.SuggestEntranceNumber("right", used);

        Assert.Equal(2, num);
    }

    [Fact]
    public void SuggestEntranceNumber_Left_SkipsUsedOdds()
    {
        var used = new HashSet<int> { 1, 3, 5 };
        var num = GeometryHelper.SuggestEntranceNumber("left", used);

        Assert.Equal(7, num);
    }

    [Fact]
    public void SuggestEntranceNumber_Right_SkipsUsedEvens()
    {
        var used = new HashSet<int> { 2, 4 };
        var num = GeometryHelper.SuggestEntranceNumber("right", used);

        Assert.Equal(6, num);
    }

    [Fact]
    public void BuildPolygonWkt_EmptyList_Throws()
    {
        Assert.Throws<ArgumentException>(() => GeometryHelper.BuildPolygonWkt([]));
    }

    [Fact]
    public void BuildPolygonWkt_FewerThanThreePoints_Throws()
    {
        Assert.Throws<ArgumentException>(() => GeometryHelper.BuildPolygonWkt(
            [new CoordDto(1.0, 2.0), new CoordDto(3.0, 4.0)]));
    }

    [Fact]
    public void SuggestEntranceNumber_ExhaustedOddSeries_DoesNotCollideWithEvens()
    {
        var used = new HashSet<int>(Enumerable.Range(0, 5000).Select(i => i * 2 + 1));

        var num = GeometryHelper.SuggestEntranceNumber("left", used);

        Assert.Equal(10001, num);
        Assert.DoesNotContain(num, used);
    }

    [Fact]
    public void SuggestEntranceNumber_ExhaustedEvenSeries_DoesNotReturnTakenBoundary()
    {
        var used = new HashSet<int>(Enumerable.Range(1, 5000).Select(i => i * 2));

        var num = GeometryHelper.SuggestEntranceNumber("right", used);

        Assert.Equal(10002, num);
        Assert.DoesNotContain(num, used);
    }
}
