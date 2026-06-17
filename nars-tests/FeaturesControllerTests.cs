using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class FeaturesControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime FixedNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static (FeaturesController, AppDbContext) CreateController(
        AppDbContext? db = null,
        IScatteredAreaService? scatteredService = null,
        IBackgroundTaskQueue? bgQueue = null,
        IConfiguration? config = null,
        IDateTimeProvider? timeProvider = null,
        IFeatureStatsService? featureStatsService = null)
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"FeaturesTest_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var context = db ?? new AppDbContext(opts);

        var cfg = config ?? CreateConfig().Object;
        var ctrl = new FeaturesController(
            context,
            scatteredService ?? Mock.Of<IScatteredAreaService>(),
            bgQueue ?? Mock.Of<IBackgroundTaskQueue>(),
            Mock.Of<ILogger<FeaturesController>>(),
            cfg,
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedNow),
            featureStatsService ?? Mock.Of<IFeatureStatsService>());

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = AuthTestHelper.CreateClaimsPrincipal(UserId, "field_worker", communeId: 1)
            }
        };

        return (ctrl, context);
    }

    private static Mock<IConfiguration> CreateConfig()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["FeatureDefaults:MaxFeatureDataSize"]).Returns("524288");
        return config;
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    // ── POST /api/save ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveFeature_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.SaveFeature(null!);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveFeature_UnknownType_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureSaveRequest("invalid_type", "main", "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveFeature_InvalidLayerForType_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureSaveRequest(FeatureTypes.Road, "invalid_layer", "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveFeature_ScatteredArea_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.Scattered, "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveFeature_DataTooLarge_Returns400()
    {
        var (ctrl, _) = CreateController();
        var largeData = new string('x', 600_000);
        var body = new FeatureSaveRequest(FeatureTypes.Road, FeatureTypes.RoadLayers.Street, "label", Json($"\"{largeData}\""));
        var result = await ctrl.SaveFeature(body);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveFeature_RoadRefNotFound_Returns400()
    {
        var (ctrl, _) = CreateController();
        var data = Json("""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""");
        var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "label", data);
        var result = await ctrl.SaveFeature(body);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SaveFeature_ValidRoad_Returns201()
    {
        var (ctrl, db) = CreateController();

        var roadId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = roadId, UserId = UserId, Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "road", UpdatedAt = FixedNow
        });
        await db.SaveChangesAsync();

        var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{roadId}}"}""");
        var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "entrance", data);

        var result = await ctrl.SaveFeature(body);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_ValidArea_Returns201()
    {
        var (ctrl, db) = CreateController();
        var data = Json("""{"coordinates":[[{"lat":36.0,"lng":3.0}]]}""");
        var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.CentralUrban, "area", data);

        var result = await ctrl.SaveFeature(body);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(1, await db.Areas.CountAsync());
    }

    // ── POST /api/clear ───────────────────────────────────────────────────

    [Fact]
    public async Task ClearFeatures_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.ClearFeatures(null!);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ClearFeatures_NotConfirmed_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: false));
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact(Skip = "InMemory provider does not support ExecuteDeleteAsync")]
    public async Task ClearFeatures_Confirmed_DeletesUserFeatures()
    {
        var (ctrl, db) = CreateController();

        var areaId = Guid.NewGuid();
        db.Areas.Add(new Area
        {
            Id = areaId, UserId = UserId, Layer = FeatureTypes.AreaLayers.CentralUrban,
            Data = "{}", Label = "a", UpdatedAt = FixedNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = areaId, FeatureType = FeatureTypes.Area });
        await db.SaveChangesAsync();

        var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: true));

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ActionResponse>(ok.Value);
        Assert.True(resp.Success);
        Assert.Equal(0, await db.Areas.CountAsync());
        Assert.Equal(0, await db.FeatureRegistry.CountAsync());
    }

    // ── POST /api/update/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateFeature_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.UpdateFeature(Guid.NewGuid(), null!);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UpdateFeature_NotFound_Returns404()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureUpdateRequest(Label: "new", Data: null);
        var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateFeature_NotOwned_Returns404()
    {
        var (ctrl, db) = CreateController();
        var otherId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = otherId, UserId = Guid.NewGuid(), Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "other", UpdatedAt = FixedNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = otherId, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var body = new FeatureUpdateRequest(Label: "new_label", Data: null);
        var result = await ctrl.UpdateFeature(otherId, body);
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UpdateFeature_DataTooLarge_Returns400()
    {
        var (ctrl, db) = CreateController();
        var fid = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = fid, UserId = UserId, Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "road", UpdatedAt = FixedNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = fid, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var largeData = new string('x', 600_000);
        var body = new FeatureUpdateRequest(Label: null, Data: JsonDocument.Parse($"\"{largeData}\"").RootElement);
        var result = await ctrl.UpdateFeature(fid, body);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact(Skip = "InMemory provider does not support ExecuteUpdateAsync")]
    public async Task UpdateFeature_Valid_Returns200()
    {
        var (ctrl, db) = CreateController();
        var fid = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = fid, UserId = UserId, Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "old", UpdatedAt = FixedNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = fid, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var body = new FeatureUpdateRequest(Label: "updated", Data: null);
        var result = await ctrl.UpdateFeature(fid, body);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<UpdateFeatureResponse>(ok.Value);
        Assert.True(resp.Success);
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────────────────

    [Fact]
    public async Task DeleteFeature_NotFound_Returns404()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.DeleteFeature(Guid.NewGuid());
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact(Skip = "InMemory provider does not support ExecuteDeleteAsync")]
    public async Task DeleteFeature_NotOwned_Returns404()
    {
        // ExecuteDeleteAsync is used in the endpoint; InMemory does not support it.
    }

    [Fact(Skip = "InMemory provider does not support ExecuteDeleteAsync")]
    public async Task DeleteFeature_Valid_Returns200()
    {
        var (ctrl, db) = CreateController();
        var fid = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = fid, UserId = UserId, Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}", Label = "road", UpdatedAt = FixedNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = fid, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var result = await ctrl.DeleteFeature(fid);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<ActionResponse>(ok.Value);
        Assert.True(resp.Success);
    }

    // ── GET /api/stats ────────────────────────────────────────────────────

    [Fact]
    public async Task GetStats_ReturnsCounts()
    {
        var statsMock = new Mock<IFeatureStatsService>();
        statsMock.Setup(s => s.GetFeatureCountsAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, long>
            {
                [FeatureTypes.Area] = 2,
                [FeatureTypes.Road] = 3
            });

        var (ctrl, _) = CreateController(featureStatsService: statsMock.Object);

        var result = await ctrl.GetStats();

        var ok = Assert.IsType<OkObjectResult>(result);
        var stats = Assert.IsType<FeatureStatsResponse>(ok.Value);
        Assert.Equal(2, stats.Area);
        Assert.Equal(3, stats.Road);
        Assert.Equal(5, stats.Total);
    }

    // ── GET /api/scattered-status ─────────────────────────────────────────

    [Fact]
    public async Task GetScatteredStatus_NoError_ReturnsOk()
    {
        var scatteredMock = new Mock<IScatteredAreaService>();
        scatteredMock.SetupGet(s => s.LastError).Returns(((DateTimeOffset Timestamp, string Message)?)null);

        var (ctrl, _) = CreateController(scatteredService: scatteredMock.Object);
        var result = ctrl.GetScatteredStatus();

        var ok = Assert.IsType<OkObjectResult>(result);
        var status = Assert.IsType<ScatteredStatusResponse>(ok.Value);
        Assert.False(status.HasError);
    }
}
