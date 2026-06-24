using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class ValidationControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static (ValidationController, AppDbContext) CreateController(
        AppDbContext? db = null,
        IValidationService? validationService = null)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"ValidationTest_{Guid.NewGuid()}")
            .Options;
        var context = db ?? new AppDbContext(opts);

        var ctrl = new ValidationController(
            context, Options.Create(new ValidationOptions()),
            validationService ?? Mock.Of<IValidationService>());

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = AuthTestHelper.CreateClaimsPrincipal(UserId, "field_worker", communeId: 1)
            }
        };

        return (ctrl, context);
    }

    // ── GET /api/validate/area/main-urban-exists ──────────────────────────

    [Fact]
    public async Task MainUrbanExists_WhenNoArea_ReturnsFalse()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.MainUrbanExists();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task MainUrbanExists_WhenAreaExists_ReturnsTrue()
    {
        var (ctrl, db) = CreateController();
        db.Areas.Add(new Area
        {
            Id = Guid.NewGuid(), UserId = UserId, Layer = FeatureTypes.AreaLayers.CentralUrban,
            Data = "{}", Label = "urban", UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await ctrl.MainUrbanExists();
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    // ── POST /api/validate/road ───────────────────────────────────────────

    [Fact]
    public async Task ValidateRoad_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.ValidateRoad(null!);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(bad.Value);
        Assert.False(resp.Valid);
    }

    [Fact]
    public async Task ValidateRoad_LessThan2Points_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new ValidateRoadRequest([new CoordDto(36.0, 3.0)]);
        var result = await ctrl.ValidateRoad(body);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(bad.Value);
        Assert.False(resp.Valid);
    }

    [Fact]
    public async Task ValidateRoad_NaNCoordinate_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new ValidateRoadRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(double.NaN, 3.0)
        ]);
        var result = await ctrl.ValidateRoad(body);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(bad.Value);
        Assert.False(resp.Valid);
    }

    [Fact]
    public async Task ValidateRoad_InfinityCoordinate_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new ValidateRoadRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(double.PositiveInfinity, 3.0)
        ]);
        var result = await ctrl.ValidateRoad(body);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(bad.Value);
        Assert.False(resp.Valid);
    }

    [Fact]
    public async Task ValidateRoad_NoExistingRoads_ReturnsValid()
    {
        var (ctrl, _) = CreateController();
        var body = new ValidateRoadRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(36.1, 3.1)
        ]);
        var result = await ctrl.ValidateRoad(body);
        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(ok.Value);
        Assert.True(resp.Valid);
    }

    [Fact]
    public async Task ValidateRoad_SharpTurn_ReturnsInvalid()
    {
        var (ctrl, db) = CreateController();
        db.Roads.Add(new Road
        {
            Id = Guid.NewGuid(), UserId = UserId, Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "r", UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var validationMock = new Mock<IValidationService>();
        validationMock.Setup(v => v.CheckRoadConnectivityAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var ctrl2 = new ValidationController(db, Options.Create(new ValidationOptions { RoadTurnAngleDegrees = 45 }), validationMock.Object);
        ctrl2.ControllerContext = ctrl.ControllerContext;

        var body = new ValidateRoadRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(36.0, 3.1),
            new CoordDto(36.1, 3.1)
        ]);
        var result = await ctrl2.ValidateRoad(body);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(ok.Value);
        Assert.False(resp.Valid);
        Assert.Contains("turn", resp.Error?.ToLowerInvariant() ?? "");
    }

    [Fact]
    public async Task ValidateRoad_NotConnected_ReturnsInvalid()
    {
        var (ctrl, db) = CreateController();

        db.Roads.Add(new Road
        {
            Id = Guid.NewGuid(), UserId = UserId, Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "r", UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var validationMock = new Mock<IValidationService>();
        validationMock.Setup(v => v.CheckRoadConnectivityAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<double>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var ctrl2 = new ValidationController(db, Options.Create(new ValidationOptions()), validationMock.Object);
        ctrl2.ControllerContext = ctrl.ControllerContext;

        var body = new ValidateRoadRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(36.1, 3.1)
        ]);
        var result = await ctrl2.ValidateRoad(body);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ValidateRoadResponse>(ok.Value);
        Assert.False(resp.Valid);
        Assert.Contains("connect", resp.Error?.ToLowerInvariant() ?? "");
    }

    // ── POST /api/validate/district ───────────────────────────────────────

    [Fact]
    public async Task ValidateDistrict_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.ValidateDistrict(null!);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ValidateDistrictResponse>(bad.Value);
        Assert.False(resp.Valid);
    }

    [Fact]
    public async Task ValidateDistrict_LessThan3Points_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new ValidateDistrictRequest([new CoordDto(36.0, 3.0), new CoordDto(36.1, 3.1)], "residential");
        var result = await ctrl.ValidateDistrict(body);
        var bad = Assert.IsType<BadRequestObjectResult>(result);
        var resp = Assert.IsType<ValidateDistrictResponse>(bad.Value);
        Assert.False(resp.Valid);
    }

    [Fact]
    public async Task ValidateDistrict_NoExistingDistricts_ReturnsValid()
    {
        var (ctrl, _) = CreateController();
        var body = new ValidateDistrictRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(36.0, 3.1),
            new CoordDto(36.1, 3.1)
        ], "residential");
        var result = await ctrl.ValidateDistrict(body);
        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ValidateDistrictResponse>(ok.Value);
        Assert.True(resp.Valid);
    }

    [Fact]
    public async Task ValidateDistrict_Overlap_ReturnsInvalid()
    {
        var (ctrl, db) = CreateController();
        db.Districts.Add(new District
        {
            Id = Guid.NewGuid(), UserId = UserId, Layer = "residential",
            Data = "{}", Label = "d", UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var validationMock = new Mock<IValidationService>();
        validationMock.Setup(v => v.CheckDistrictOverlapAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var (ctrl2, _) = CreateController(db: db, validationService: validationMock.Object);

        var body = new ValidateDistrictRequest([
            new CoordDto(36.0, 3.0),
            new CoordDto(36.0, 3.1),
            new CoordDto(36.1, 3.1)
        ], "residential");
        var result = await ctrl2.ValidateDistrict(body);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ValidateDistrictResponse>(ok.Value);
        Assert.False(resp.Valid);
        Assert.Contains("overlap", resp.Error?.ToLowerInvariant() ?? "");
    }

    // ── GET /api/validate/districts/coverage ──────────────────────────────

    [Fact]
    public async Task DistrictsCoverage_NoUrbanAreas_ReturnsCovered()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.DistrictsCoverage();
        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<DistrictCoverageResponse>(ok.Value);
        Assert.True(resp.Covered);
    }

    [Fact]
    public async Task DistrictsCoverage_NoDistricts_ReturnsNotCovered()
    {
        var (ctrl, db) = CreateController();
        db.Areas.Add(new Area
        {
            Id = Guid.NewGuid(), UserId = UserId, Layer = FeatureTypes.AreaLayers.CentralUrban,
            Data = "{}", Label = "urban", UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var result = await ctrl.DistrictsCoverage();
        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<DistrictCoverageResponse>(ok.Value);
        Assert.False(resp.Covered);
    }
}
