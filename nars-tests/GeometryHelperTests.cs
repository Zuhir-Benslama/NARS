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
    public void FindNearestSegmentIndex_PointAtStart()
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
    public void DetermineSide_Left()
    {
        // Moving east (0,0)->(0,1), point is north (above) -> left
        var side = GeometryHelper.DetermineSide(1.0, 0.5, 0.0, 0.0, 0.0, 1.0);

        Assert.Equal("left", side);
    }

    [Fact]
    public void DetermineSide_Right()
    {
        // Moving east (0,0)->(0,1), point is south (below) -> right
        var side = GeometryHelper.DetermineSide(-1.0, 0.5, 0.0, 0.0, 0.0, 1.0);

        Assert.Equal("right", side);
    }

    [Fact]
    public void DetermineSide_OnLine_ReturnsLeft()
    {
        // Cross product exactly zero -> left
        var side = GeometryHelper.DetermineSide(0.0, 0.5, 0.0, 0.0, 0.0, 1.0);

        Assert.Equal("left", side);
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

        Assert.Equal(0.0, angle);
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
}
