using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class FeaturesControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static (FeaturesController, AppDbContext) CreateController(
        AppDbContext? db = null,
        IBackgroundTaskQueue? bgQueue = null,
        IDateTimeProvider? timeProvider = null,
        IFeatureStatsService? featureStatsService = null)
    {
        var context = db ?? CreateInMemoryDb("FeaturesTest");

        var ctrl = new FeaturesController(
            new FeatureService(context),
            bgQueue ?? Mock.Of<IBackgroundTaskQueue>(),
            Mock.Of<ILogger<FeaturesController>>(),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            featureStatsService ?? Mock.Of<IFeatureStatsService>(),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = AuthTestHelper.CreateClaimsPrincipal(UserId, UserRoles.FieldWorker, communeId: 1)
                }
            }
        };

        return (ctrl, context);
    }

    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    // ── POST /api/save ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveFeature_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.SaveFeature(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_UnknownType_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureSaveRequest("invalid_type", "main", "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_InvalidLayerForType_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureSaveRequest(FeatureTypes.Road, "invalid_layer", "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_ScatteredArea_Returns400()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.Scattered, "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_DataTooLarge_Returns400()
    {
        var (ctrl, _) = CreateController();
        var largeData = new string('x', 600_000);
        var body = new FeatureSaveRequest(FeatureTypes.Road, FeatureTypes.RoadLayers.Street, "label", Json($"\"{largeData}\""));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_RoadRefNotFound_Returns400()
    {
        var (ctrl, _) = CreateController();
        var data = Json("""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"}""");
        var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "label", data);
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_ValidRoad_Returns201()
    {
        var (ctrl, db) = CreateController();

        var roadId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = UserId,
            Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}",
            Label = "road",
            UpdatedAt = FixedUtcNow
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

    [Fact]
    public async Task SaveFeature_Area_QueuesScatteredRefresh()
    {
        var bgQueueMock = new Mock<IBackgroundTaskQueue>();
        bgQueueMock.Setup(x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()))
            .Returns(ValueTask.CompletedTask);

        var (ctrl, _) = CreateController(bgQueue: bgQueueMock.Object);
        var data = Json("""{"coordinates":[[{"lat":36.0,"lng":3.0}]]}""");
        var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.CentralUrban, "area", data);

        await ctrl.SaveFeature(body);

        bgQueueMock.Verify(
            x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()),
            Times.Once);
    }

    [Fact]
    public async Task SaveFeature_NonArea_DoesNotQueueScatteredRefresh()
    {
        var bgQueueMock = new Mock<IBackgroundTaskQueue>();
        bgQueueMock.Setup(x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()))
            .Returns(ValueTask.CompletedTask);

        var (ctrl, _) = CreateController(bgQueue: bgQueueMock.Object);
        var data = Json("""{"coordinates":[{"lat":36.0,"lng":3.0}]}""");
        var body = new FeatureSaveRequest(FeatureTypes.Road, FeatureTypes.RoadLayers.Street, "road", data);

        await ctrl.SaveFeature(body);

        bgQueueMock.Verify(
            x => x.QueueBackgroundWorkItemAsync(It.IsAny<Func<IServiceProvider, CancellationToken, Task>>()),
            Times.Never);
    }

    // ── POST /api/clear ───────────────────────────────────────────────────

    [Fact]
    public async Task ClearFeatures_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.ClearFeatures(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task ClearFeatures_NotConfirmed_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: false));
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    // ── POST /api/update/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateFeature_NullBody_Returns400()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.UpdateFeature(Guid.NewGuid(), null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_NotFound_Returns404()
    {
        var (ctrl, _) = CreateController();
        var body = new FeatureUpdateRequest(Label: "new", Data: null);
        var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_NotOwned_Returns404()
    {
        var (ctrl, db) = CreateController();
        var otherId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = otherId,
            UserId = Guid.NewGuid(),
            Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}",
            Label = "other",
            UpdatedAt = FixedUtcNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = otherId, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var body = new FeatureUpdateRequest(Label: "new_label", Data: null);
        var result = await ctrl.UpdateFeature(otherId, body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_DataTooLarge_Returns400()
    {
        var (ctrl, db) = CreateController();
        var fid = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = fid,
            UserId = UserId,
            Layer = FeatureTypes.RoadLayers.Street,
            Data = "{}",
            Label = "road",
            UpdatedAt = FixedUtcNow
        });
        db.FeatureRegistry.Add(new FeatureRegistry { Id = fid, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var largeData = new string('x', 600_000);
        var body = new FeatureUpdateRequest(Label: null, Data: JsonDocument.Parse($"\"{largeData}\"").RootElement);
        var result = await ctrl.UpdateFeature(fid, body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────────────────

    [Fact]
    public async Task DeleteFeature_NotFound_Returns404()
    {
        var (ctrl, _) = CreateController();
        var result = await ctrl.DeleteFeature(Guid.NewGuid());
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
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

}
