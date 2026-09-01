using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
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
public class SpatialControllerServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private Guid _userId;

    protected override async Task SeedAsync()
    {
        _userId = await CreateUserAsync();
    }

    private SpatialController CreateController(IScatteredAreaService? scatteredService = null)
    {
        var ctrl = new SpatialController(
            new RoadQueryService(Db),
            scatteredService ?? Mock.Of<IScatteredAreaService>(),
            new EntranceQueryService(Fixture.CreateDbContextFactory()),
            Mock.Of<IWebHostEnvironment>(),
            Mock.Of<ILogger<SpatialController>>());
        AuthTestHelper.SetUser(ctrl, _userId, UserRoles.FieldWorker, communeId: 1);
        return ctrl;
    }

    private async Task<Guid> CreateUserAsync()
    {
        await SeedData.SeedBasicLocationsAsync(Db);
        var user = await SeedData.CreateUserAsync(Db, UserRoles.FieldWorker, communeId: 1, name: "Spatial Test User");
        return user.Id;
    }

    [Fact]
    public async Task GetRoadSide_ValidRequest_ReturnsCorrectSide()
    {
        var controller = CreateController();
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = await TestData.AddRoadAsync(Db, _userId, coords, registerInFeatureRegistry: true);

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
    public void GetScatteredStatus_RecordedError_IsReported()
    {
        var error = (Timestamp: FixedUtcNowOffset, Message: "An error occurred during computation.");
        var scatteredMock = new Mock<IScatteredAreaService>(MockBehavior.Strict);
        scatteredMock.Setup(s => s.GetLastError(_userId, 1)).Returns(error);

        var controller = CreateController(scatteredMock.Object);
        var result = controller.GetScatteredStatus();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredStatusResponse>(ok.Value);
        Assert.True(resp.HasError);
        Assert.Equal("An error occurred during computation.", resp.LastErrorMessage);
        Assert.Equal(FixedUtcNowOffset.ToString(JsonHelper.IsoDateFormat), resp.LastErrorTime);
    }

    [Fact]
    public async Task RefreshScattered_ValidRequest_Returns200()
    {
        var unit = Mock.Of<IScatteredAreaService>(s =>
            s.RefreshAsync(_userId, 1, It.IsAny<CancellationToken>()) == Task.FromResult<string?>("{}"));

        var controller = CreateController(unit);

        var result = await controller.RefreshScattered();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredRefreshResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal("{}", resp.GeoJson);
    }

    [Fact]
    public async Task RefreshScattered_FailedRefresh_Returns500()
    {
        var unit = new Mock<IScatteredAreaService>(MockBehavior.Strict);
        unit.Setup(s => s.RefreshAsync(_userId, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        unit.Setup(s => s.GetLastError(_userId, 1)).Returns((FixedUtcNowOffset, "error"));

        var controller = CreateController(unit.Object);

        var result = await controller.RefreshScattered();

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objResult.StatusCode);
    }

    [Fact]
    public async Task RefreshScattered_NoCommuneId_Returns400()
    {
        // Intentionally bypasses CreateController() — this test requires
        // NationalAdmin with no communeId, whereas the helper sets FieldWorker+communeId:1.
        var controller = new SpatialController(
            new RoadQueryService(Db),
            Mock.Of<IScatteredAreaService>(),
            new EntranceQueryService(Fixture.CreateDbContextFactory()),
            Mock.Of<IWebHostEnvironment>(),
            Mock.Of<ILogger<SpatialController>>());
        AuthTestHelper.SetUser(controller, _userId, UserRoles.NationalAdmin);

        var result = await controller.RefreshScattered();

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }
}
