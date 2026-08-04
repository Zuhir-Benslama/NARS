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
    private static FeaturesController CreateController(
        AppDbContext db,
        IBackgroundTaskQueue? bgQueue = null,
        IDateTimeProvider? timeProvider = null,
        IFeatureStatsService? featureStatsService = null,
        IFeatureService? featureService = null)
    {
        var ctrl = new FeaturesController(
            featureService ?? new FeatureService(db),
            bgQueue ?? Mock.Of<IBackgroundTaskQueue>(),
            Mock.Of<ILogger<FeaturesController>>(),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            featureStatsService ?? Mock.Of<IFeatureStatsService>(),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        AuthTestHelper.SetUser(ctrl, UserId, UserRoles.FieldWorker, communeId: 1);

        return ctrl;
    }

    private static JsonElement Json(string raw) => System.Text.Json.JsonSerializer.Deserialize<JsonElement>(raw);

    // ── POST /api/save ────────────────────────────────────────────────────

    [Fact]
    public async Task SaveFeature_NullBody_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var result = await ctrl.SaveFeature(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_UnknownType_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var body = new FeatureSaveRequest("invalid_type", "main", "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_InvalidLayerForType_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var body = new FeatureSaveRequest(FeatureTypes.Road, "invalid_layer", "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_ScatteredArea_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.Scattered, "label", Json("{}"));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_DataTooLarge_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var largeData = new string('x', OversizedDataLength);
        var body = new FeatureSaveRequest(FeatureTypes.Road, FeatureTypes.RoadLayers.Street, "label", Json($"\"{largeData}\""));
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_RoadRefNotFound_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{Guid.NewGuid()}}"}""");
        var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "label", data);
        var result = await ctrl.SaveFeature(body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_ValidRoad_Returns201()
    {
        using var db = CreateInMemoryDb("FeaturesTest");

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

        var ctrl = CreateController(db);
        var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{roadId}}"}""");
        var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "entrance", data);

        var result = await ctrl.SaveFeature(body);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task SaveFeature_ValidArea_Returns201()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);

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

        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db, bgQueue: bgQueueMock.Object);
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

        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db, bgQueue: bgQueueMock.Object);
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
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var result = await ctrl.ClearFeatures(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task ClearFeatures_NotConfirmed_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: false));
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task ClearFeatures_Confirmed_Returns200()
    {
        var featureServiceMock = new Mock<IFeatureService>();
        featureServiceMock.Setup(s => s.ClearAllFeaturesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db, featureService: featureServiceMock.Object);

        var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: true));
        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, ok.StatusCode);
    }

    // ── POST /api/update/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateFeature_NullBody_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var result = await ctrl.UpdateFeature(Guid.NewGuid(), null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_NotFound_Returns404()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
        var body = new FeatureUpdateRequest(Label: "new", Data: null);
        var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_NotOwned_Returns404()
    {
        using var db = CreateInMemoryDb("FeaturesTest");

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

        var ctrl = CreateController(db);
        var body = new FeatureUpdateRequest(Label: "new_label", Data: null);
        var result = await ctrl.UpdateFeature(otherId, body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objResult.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_DataTooLarge_Returns400()
    {
        using var db = CreateInMemoryDb("FeaturesTest");

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

        var ctrl = CreateController(db);
        var largeData = new string('x', OversizedDataLength);
        var body = new FeatureUpdateRequest(Label: null, Data: System.Text.Json.JsonSerializer.Deserialize<JsonElement>($"\"{largeData}\""));
        var result = await ctrl.UpdateFeature(fid, body);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    // ── DELETE /api/delete/{id} ───────────────────────────────────────────

    [Fact]
    public async Task DeleteFeature_HappyPath_Returns204()
    {
        var fid = Guid.NewGuid();
        var featureServiceMock = new Mock<IFeatureService>();
        featureServiceMock.Setup(s => s.GetFeatureTypeAsync(fid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeatureTypes.Area);
        featureServiceMock.Setup(s => s.DeleteFeatureAsync(fid, UserId, FeatureTypes.Area, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db, featureService: featureServiceMock.Object);

        var result = await ctrl.DeleteFeature(fid);
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task DeleteFeature_NotFound_Returns404()
    {
        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db);
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

        using var db = CreateInMemoryDb("FeaturesTest");
        var ctrl = CreateController(db, featureStatsService: statsMock.Object);

        var result = await ctrl.GetStats();

        var ok = Assert.IsType<OkObjectResult>(result);
        var stats = Assert.IsType<FeatureStatsResponse>(ok.Value);
        Assert.Equal(2, stats.Area);
        Assert.Equal(3, stats.Road);
        Assert.Equal(5, stats.Total);
    }

}
