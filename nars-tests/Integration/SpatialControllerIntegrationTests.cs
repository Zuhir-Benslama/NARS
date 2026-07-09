using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Integration;

[Collection("PostgreSQL Integration")]
public class SpatialControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private AppDbContext _db = null!;
    private Guid _userId;

    public SpatialControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        _userId = await CreateUserAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _fixture.CleanTablesAsync();
    }

    private SpatialController CreateController(IScatteredAreaService? scatteredService = null)
    {
        var ctrl = new SpatialController(
            new RoadQueryService(_db),
            scatteredService ?? Mock.Of<IScatteredAreaService>(),
            new EntranceQueryService(_db));
        var httpContext = new DefaultHttpContext
        {
            User = AuthTestHelper.CreateClaimsPrincipal(_userId, "field_worker", communeId: 1)
        };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return ctrl;
    }

    private async Task<Guid> CreateUserAsync()
    {
        var user = await SeedData.CreateUserAsync(_db, "field_worker", communeId: 1, name: "Spatial Test User");
        await SeedData.SeedBasicLocationsAsync(_db);
        return user.Id;
    }

    private Guid AddRoad(AppDbContext db, Guid ownerId, string coordsJson)
    {
        var id = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = id,
            UserId = ownerId,
            Data = coordsJson,
            Label = "Test Integration Road",
            Layer = "street",
            UpdatedAt = FixedUtcNow,
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = id, FeatureType = FeatureTypes.Road });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task GetRoadSide_ValidRequest_ReturnsCorrectSide()
    {
        var controller = CreateController();
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = AddRoad(_db, _userId, coords);

        var result = await controller.GetRoadSide(new RoadSideRequest(
            RoadId: roadId, Lat: 36.5, Lng: 3.0));

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<RoadSideResponse>(ok.Value);
        Assert.Equal("left", resp.Side);
        Assert.True(resp.SuggestedNumber > 0);
    }

    [Fact]
    public async Task GetRoadSide_RoadNotFound_Returns404()
    {
        var controller = CreateController();
        var result = await controller.GetRoadSide(new RoadSideRequest(
            RoadId: Guid.NewGuid(), Lat: 36.0, Lng: 3.0));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task GetRoadSide_NullBody_Returns400()
    {
        var controller = CreateController();
        var result = await controller.GetRoadSide(null!);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetScatteredStatus_ReturnsStatus()
    {
        var controller = CreateController();
        var result = controller.GetScatteredStatus();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredStatusResponse>(ok.Value);
        Assert.NotNull(resp);
    }

    [Fact]
    public async Task RefreshScattered_ValidRequest_Returns200()
    {
        var scatteredMock = new Mock<IScatteredAreaService>(MockBehavior.Strict);
        scatteredMock.Setup(s => s.RefreshAsync(_userId, 1, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var controller = CreateController(scatteredMock.Object);

        var result = await controller.RefreshScattered();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredRefreshResponse>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task RefreshScattered_NoCommuneId_Returns400()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = AuthTestHelper.CreateClaimsPrincipal(_userId, "national_admin", communeId: null);
        var controller = new SpatialController(
            new RoadQueryService(_db),
            Mock.Of<IScatteredAreaService>(),
            new EntranceQueryService(_db))
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.RefreshScattered();

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }
}
