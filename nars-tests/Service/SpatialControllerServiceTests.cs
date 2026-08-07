using Microsoft.AspNetCore.Hosting;
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

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class SpatialControllerServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture = fixture;
    private AppDbContext _db = null!;
    private Guid _userId;

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        _userId = await CreateUserAsync();
    }

    public async Task DisposeAsync()
    {
        try { await _db.DisposeAsync(); }
        finally { await _fixture.CleanTablesAsync(); }
    }

    private SpatialController CreateController(IScatteredAreaService? scatteredService = null)
    {
        var ctrl = new SpatialController(
            new RoadQueryService(_db),
            scatteredService ?? Mock.Of<IScatteredAreaService>(),
            new EntranceQueryService(_db),
            Mock.Of<IWebHostEnvironment>());
        var httpContext = new DefaultHttpContext
        {
            User = AuthTestHelper.CreateClaimsPrincipal(_userId, UserRoles.FieldWorker, communeId: 1)
        };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return ctrl;
    }

    private async Task<Guid> CreateUserAsync()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.FieldWorker, communeId: 1, name: "Spatial Test User");
        await SeedData.SeedBasicLocationsAsync(_db);
        return user.Id;
    }

    [Fact]
    public async Task GetRoadSide_ValidRequest_ReturnsCorrectSide()
    {
        var controller = CreateController();
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = await TestData.AddRoadAsync(_db, _userId, coords, registerInFeatureRegistry: true);

        var result = await controller.GetRoadSide(new RoadSideRequest(
            RoadId: roadId, Lat: 36.5, Lng: 3.0));

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<RoadSideResponse>(ok.Value);
        Assert.Equal("left", resp.Side);
        Assert.Equal(1, resp.SuggestedNumber); // no entrances used -> first odd number
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
    public void GetScatteredStatus_ReturnsStatus()
    {
        var controller = CreateController();
        var result = controller.GetScatteredStatus();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredStatusResponse>(ok.Value);
        Assert.False(resp.HasError);
        Assert.Null(resp.LastErrorMessage);
        Assert.Null(resp.LastErrorTime);
    }

    [Fact]
    public async Task RefreshScattered_ValidRequest_Returns200()
    {
        var scatteredMock = new Mock<IScatteredAreaService>(MockBehavior.Strict);
        scatteredMock.Setup(s => s.RefreshAsync(_userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var controller = CreateController(scatteredMock.Object);

        var result = await controller.RefreshScattered();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredRefreshResponse>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task RefreshScattered_FailedRefresh_Returns500()
    {
        var scatteredMock = new Mock<IScatteredAreaService>(MockBehavior.Strict);
        scatteredMock.Setup(s => s.RefreshAsync(_userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        scatteredMock.SetupGet(s => s.LastError)
            .Returns((FixedUtcNowOffset, "Simulated refresh failure"));

        var controller = CreateController(scatteredMock.Object);

        var result = await controller.RefreshScattered();

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objResult.StatusCode);
    }

    [Fact]
    public async Task RefreshScattered_NoCommuneId_Returns400()
    {
        var httpContext = new DefaultHttpContext
        {
            User = AuthTestHelper.CreateClaimsPrincipal(_userId, UserRoles.NationalAdmin, communeId: null)
        };
        var controller = new SpatialController(
            new RoadQueryService(_db),
            Mock.Of<IScatteredAreaService>(),
            new EntranceQueryService(_db),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        var result = await controller.RefreshScattered();

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }
}
