using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Data;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class SpatialControllerTests
{
    private static (SpatialController, AppDbContext) CreateController(
        IScatteredAreaService? scatteredService = null,
        IEntranceQueryService? entranceQuery = null,
        Guid? userId = null)
    {
        var db = CreateInMemoryDb("SpatialTest");

        var ctrl = new SpatialController(
            new RoadQueryService(db),
            scatteredService ?? Mock.Of<IScatteredAreaService>(),
            entranceQuery ?? Mock.Of<IEntranceQueryService>());

        var uid = userId ?? Guid.NewGuid();
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = AuthTestHelper.CreateClaimsPrincipal(uid, "field_worker", communeId: 1)
            }
        };

        return (ctrl, db);
    }

    private static Guid AddRoad(AppDbContext db, Guid userId, string coordsJson)
    {
        var id = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = id,
            UserId = userId,
            Data = coordsJson,
            Label = "Test Road",
            Layer = "main",
            UpdatedAt = FixedUtcNow
        });
        db.SaveChanges();
        return id;
    }

    // ── POST /api/road-side ───────────────────────────────────────────────

    [Fact]
    public async Task GetRoadSide_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.GetRoadSide(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetRoadSide_RoadNotFound_Returns404()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.GetRoadSide(new RoadSideRequest(
            RoadId: Guid.NewGuid(), Lat: 36.0, Lng: 3.0));
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task GetRoadSide_InsufficientCoords_Returns400()
    {
        var uid = Guid.NewGuid();
        var (ctrl, db) = CreateController(userId: uid);

        var roadId = AddRoad(db, uid, /* single point */ """{"coordinates":[{"lat":36.0,"lng":3.0}]}""");

        var result = await ctrl.GetRoadSide(new RoadSideRequest(RoadId: roadId, Lat: 36.0, Lng: 3.0));
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Theory]
    [InlineData(double.NaN, 3.0)]
    [InlineData(double.PositiveInfinity, 3.0)]
    [InlineData(double.NegativeInfinity, 3.0)]
    public async Task GetRoadSide_InvalidCoordinates_Returns400(double lat, double lng)
    {
        var uid = Guid.NewGuid();
        var (ctrl, db) = CreateController(
            userId: uid,
            entranceQuery: Mock.Of<IEntranceQueryService>(x =>
                x.GetUsedEntranceNumbersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new HashSet<int>())));
        var roadId = AddRoad(db, uid, """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""");

        var result = await ctrl.GetRoadSide(new RoadSideRequest(
            RoadId: roadId, Lat: lat, Lng: lng));
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetRoadSide_MissingCoordinatesProperty_Returns400()
    {
        var uid = Guid.NewGuid();
        var (ctrl, db) = CreateController(userId: uid);

        // Road data without a "coordinates" property
        var roadId = AddRoad(db, uid, """{"foo":"bar"}""");

        var result = await ctrl.GetRoadSide(new RoadSideRequest(RoadId: roadId, Lat: 36.0, Lng: 3.0));
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Theory]
    [InlineData(36.5, 3.0, "left")]   // marker above the segment
    [InlineData(36.3, 3.0, "right")]  // marker below the segment
    public async Task GetRoadSide_ReturnsCorrectSide(double markerLat, double markerLng, string expectedSide)
    {
        var uid = Guid.NewGuid();
        var (ctrl, db) = CreateController(
            userId: uid,
            entranceQuery: Mock.Of<IEntranceQueryService>(x =>
                x.GetUsedEntranceNumbersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
                == Task.FromResult(new HashSet<int>())));

        // Horizontal segment from (36.4, 2.9) to (36.4, 3.1)
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = AddRoad(db, uid, coords);

        var result = await ctrl.GetRoadSide(new RoadSideRequest(RoadId: roadId, Lat: markerLat, Lng: markerLng));

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<RoadSideResponse>(ok.Value);
        Assert.Equal(expectedSide, resp.Side);
    }

    [Fact]
    public async Task GetRoadSide_SuggestsNextAvailableNumber()
    {
        var uid = Guid.NewGuid();
        var usedNumbers = new HashSet<int> { 1, 3, 5 };
        var (ctrl, db) = CreateController(
            userId: uid,
            entranceQuery: Mock.Of<IEntranceQueryService>(x =>
                x.GetUsedEntranceNumbersAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>())
                == Task.FromResult(usedNumbers)));

        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = AddRoad(db, uid, coords);

        // Marker above the segment -> left -> odd numbers
        var result = await ctrl.GetRoadSide(new RoadSideRequest(RoadId: roadId, Lat: 36.5, Lng: 3.0));

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<RoadSideResponse>(ok.Value);
        Assert.Equal("left", resp.Side);
        Assert.Equal(7, resp.SuggestedNumber); // 1,3,5 used, next odd is 7
    }

    // ── POST /api/areas/refresh-scattered ─────────────────────────────────

    [Fact]
    public async Task RefreshScattered_NoCommuneId_Returns400()
    {
        var uid = Guid.NewGuid();
        var (ctrl, _) = CreateController(userId: uid);

        // Override claims — no commune_id
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = AuthTestHelper.CreateClaimsPrincipal(uid, "national_admin", communeId: null)
            }
        };

        var result = await ctrl.RefreshScattered();
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task RefreshScattered_Valid_Returns200()
    {
        var uid = Guid.NewGuid();
        var scatteredMock = new Mock<IScatteredAreaService>();
        scatteredMock.Setup(s => s.RefreshAsync(uid, 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var (ctrl, _) = CreateController(userId: uid, scatteredService: scatteredMock.Object);

        var result = await ctrl.RefreshScattered();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredRefreshResponse>(ok.Value);
        Assert.True(resp.Success);
    }
}
