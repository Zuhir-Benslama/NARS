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
    private readonly AppDbContext _db;
    private readonly SpatialController _controller;
    private Guid _userId;

    public SpatialControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
        _db = fixture.CreateDbContext();

        var entranceQuery = new EntranceQueryService(_db);

        _controller = new SpatialController(
            _db,
            Mock.Of<IScatteredAreaService>(),
            entranceQuery);
    }

    public async Task InitializeAsync()
    {
        _userId = await CreateUserAsync();
        SetAuthenticatedUser(_userId, 1);
    }

    public async Task DisposeAsync() => await _fixture.CleanTablesAsync();

    private async Task<Guid> CreateUserAsync()
    {
        var userId = Guid.NewGuid();
        await _db.Users.AddAsync(new User
        {
            Id = userId,
            Name = "Spatial Test User",
            Email = $"spatial-{userId:N}@test.com",
            Phone = DefaultPhone,
            Username = $"spatial_{userId:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            CommuneId = 1,
        });

        await SeedData.SeedBasicLocationsAsync(_db);
        await _db.SaveChangesAsync();
        return userId;
    }

    private void SetAuthenticatedUser(Guid userId, int communeId)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = AuthTestHelper.CreateClaimsPrincipal(userId, "field_worker", communeId: communeId);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
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
            UpdatedAt = DateTime.UtcNow,
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = id, FeatureType = FeatureTypes.Road });
        db.SaveChanges();
        return id;
    }

    [Fact]
    public async Task GetRoadSide_ValidRequest_ReturnsCorrectSide()
    {
        var coords = """{"coordinates":[{"lat":36.4,"lng":2.9},{"lat":36.4,"lng":3.1}]}""";
        var roadId = AddRoad(_db, _userId, coords);

        var result = await _controller.GetRoadSide(new RoadSideRequest(
            RoadId: roadId, Lat: 36.5, Lng: 3.0));

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<RoadSideResponse>(ok.Value);
        Assert.Equal("left", resp.Side);
        Assert.True(resp.SuggestedNumber > 0);
    }

    [Fact]
    public async Task GetRoadSide_RoadNotFound_Returns404()
    {
        var result = await _controller.GetRoadSide(new RoadSideRequest(
            RoadId: Guid.NewGuid(), Lat: 36.0, Lng: 3.0));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task GetRoadSide_NullBody_Returns400()
    {
        var result = await _controller.GetRoadSide(null!);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task GetScatteredStatus_ReturnsStatus()
    {
        var result = _controller.GetScatteredStatus();

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

        var httpContext = new DefaultHttpContext();
        httpContext.User = AuthTestHelper.CreateClaimsPrincipal(_userId, "field_worker", communeId: 1);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.RefreshScattered();

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ScatteredRefreshResponse>(ok.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task RefreshScattered_NoCommuneId_Returns400()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.User = AuthTestHelper.CreateClaimsPrincipal(_userId, "national_admin", communeId: null);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.RefreshScattered();

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }
}
