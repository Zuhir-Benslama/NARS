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
        IDateTimeProvider? timeProvider = null,
        IFeatureStatsService? featureStatsService = null,
        IFeatureService? featureService = null,
        IDbContextFactory<AppDbContext>? factory = null)
    {
        var ctrl = new FeaturesController(
            featureService ?? new FeatureService(factory ?? new TestDbContextFactory(db), Mock.Of<IBackgroundTaskQueue>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<ILogger<FeatureService>>()),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow),
            featureStatsService ?? Mock.Of<IFeatureStatsService>(),
            Mock.Of<INumberEntrancesService>(),
            Mock.Of<ILogger<FeaturesController>>(),
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
    public async Task SaveFeature_UnknownType_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var body = new FeatureSaveRequest("invalid_type", "main", "label", Json("{}"));
            var result = await ctrl.SaveFeature(body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task SaveFeature_InvalidLayerForType_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var body = new FeatureSaveRequest(FeatureTypes.Road, "invalid_layer", "label", Json("{}"));
            var result = await ctrl.SaveFeature(body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task SaveFeature_ScatteredArea_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.Scattered, "label", Json("{}"));
            var result = await ctrl.SaveFeature(body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task SaveFeature_DataTooLarge_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var largeData = new string('x', OversizedDataLength);
            var body = new FeatureSaveRequest(FeatureTypes.Road, FeatureTypes.RoadLayers.Street, "label", Json($"\"{largeData}\""));
            var result = await ctrl.SaveFeature(body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task SaveFeature_RoadRefNotFound_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{Guid.NewGuid()}}"}""");
            var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "label", data);
            var result = await ctrl.SaveFeature(body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task SaveFeature_ValidRoad_Returns201()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
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

            var ctrl = CreateController(db, factory: factory);
            var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{roadId}}"}""");
            var body = new FeatureSaveRequest(FeatureTypes.HouseEntrance, FeatureTypes.HouseEntranceLayers.Main, "entrance", data);

            var result = await ctrl.SaveFeature(body);

            var created = Assert.IsType<ObjectResult>(result);
            Assert.Equal(201, created.StatusCode);
            Assert.Equal(1, await db.HouseEntrances.CountAsync());
        }
    }

    [Fact]
    public async Task SaveFeature_ValidArea_Returns201()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);

            var data = Json("""{"coordinates":[[{"lat":36.0,"lng":3.0}]]}""");
            var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.CentralUrban, "area", data);

            var result = await ctrl.SaveFeature(body);

            var created = Assert.IsType<ObjectResult>(result);
            Assert.Equal(201, created.StatusCode);
            Assert.Equal(1, await db.Areas.CountAsync());
        }
    }

    [Fact]
    public async Task SaveFeature_Area_QueuesScatteredRefresh()
    {
        var featureServiceMock = new Mock<IFeatureService>();
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);
            var data = Json("""{"coordinates":[[{"lat":36.0,"lng":3.0}]]}""");
            var body = new FeatureSaveRequest(FeatureTypes.Area, FeatureTypes.AreaLayers.CentralUrban, "area", data);

            await ctrl.SaveFeature(body);

            featureServiceMock.Verify(
                s => s.QueueScatteredRefreshAsync(UserId, 1),
                Times.Once);
        }
    }

    [Fact]
    public async Task SaveFeature_NonArea_DoesNotQueueScatteredRefresh()
    {
        var featureServiceMock = new Mock<IFeatureService>();
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);
            var data = Json("""{"coordinates":[{"lat":36.0,"lng":3.0}]}""");
            var body = new FeatureSaveRequest(FeatureTypes.Road, FeatureTypes.RoadLayers.Street, "road", data);

            await ctrl.SaveFeature(body);

            featureServiceMock.Verify(
                s => s.QueueScatteredRefreshAsync(It.IsAny<Guid>(), It.IsAny<int?>()),
                Times.Never);
        }
    }

    // ── POST /api/clear ───────────────────────────────────────────────────

    [Fact]
    public async Task ClearFeatures_NotConfirmed_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: false));
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task ClearFeatures_Confirmed_Returns200()
    {
        var featureServiceMock = new Mock<IFeatureService>();
        featureServiceMock.Setup(s => s.ClearAllFeaturesAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);

            var result = await ctrl.ClearFeatures(new ClearFeaturesRequest(Confirm: true));
            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Equal(200, ok.StatusCode);
            featureServiceMock.Verify(
                s => s.ClearAllFeaturesAsync(UserId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    // ── POST /api/update/{id} ─────────────────────────────────────────────

    [Fact]
    public async Task UpdateFeature_NotFound_Returns404()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var body = new FeatureUpdateRequest(Label: "new", Data: null);
            var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(404, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task UpdateFeature_NotOwned_Returns404()
    {
        // Ownership is enforced by UpdateFeatureAsync's WHERE user_id clause
        // (there is no dedicated ownership check in the controller), so a
        // feature belonging to another user is invisible: the update reports
        // "not found". Mock the service to model that scoping.
        var featureServiceMock = new Mock<IFeatureService>();
        featureServiceMock.Setup(s => s.GetFeatureTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeatureTypes.Road);
        featureServiceMock.Setup(s => s.UpdateFeatureAsync(It.IsAny<UpdateFeatureCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);
            var body = new FeatureUpdateRequest(Label: "new_label", Data: null);
            var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(404, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task UpdateFeature_DataTooLarge_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
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

            var ctrl = CreateController(db, factory: factory);
            var largeData = new string('x', OversizedDataLength);
            var body = new FeatureUpdateRequest(Label: null, Data: System.Text.Json.JsonSerializer.Deserialize<JsonElement>($"\"{largeData}\""));
            var result = await ctrl.UpdateFeature(fid, body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task UpdateFeature_HouseEntranceRoadRefNotFound_Returns400()
    {
        // House-entrance updates can relink via roadDbId in the payload; a road
        // the user does not own (or that does not exist) must be rejected with
        // 400, mirroring the Save path.
        var featureServiceMock = new Mock<IFeatureService>();
        featureServiceMock.Setup(s => s.GetFeatureTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeatureTypes.HouseEntrance);
        featureServiceMock.Setup(s => s.RoadExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);
            var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{Guid.NewGuid()}}"}""");
            var body = new FeatureUpdateRequest(Label: "new_label", Data: data);
            var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
            featureServiceMock.Verify(
                s => s.RoadExistsAsync(It.IsAny<Guid>(), UserId, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task UpdateFeature_HouseEntranceRoadRefOk_ReachesService()
    {
        var featureServiceMock = new Mock<IFeatureService>();
        featureServiceMock.Setup(s => s.GetFeatureTypeAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(FeatureTypes.HouseEntrance);
        featureServiceMock.Setup(s => s.RoadExistsAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        featureServiceMock.Setup(s => s.UpdateFeatureAsync(It.IsAny<UpdateFeatureCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);
            var roadId = Guid.NewGuid();
            var data = Json($$"""{"coordinates":[{"lat":36.0,"lng":3.0}],"roadDbId":"{{roadId}}"}""");
            var body = new FeatureUpdateRequest(Label: "new_label", Data: data);
            var result = await ctrl.UpdateFeature(Guid.NewGuid(), body);
            Assert.IsType<OkObjectResult>(result);
            featureServiceMock.Verify(
                s => s.RoadExistsAsync(roadId, UserId, It.IsAny<CancellationToken>()), Times.Once);
            featureServiceMock.Verify(
                s => s.UpdateFeatureAsync(It.IsAny<UpdateFeatureCommand>(), It.IsAny<CancellationToken>()), Times.Once);
        }
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
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureService: featureServiceMock.Object, factory: factory);

            var result = await ctrl.DeleteFeature(fid);
            Assert.IsType<NoContentResult>(result);
            featureServiceMock.Verify(
                s => s.GetFeatureTypeAsync(fid, It.IsAny<CancellationToken>()), Times.Once);
            featureServiceMock.Verify(
                s => s.DeleteFeatureAsync(fid, UserId, FeatureTypes.Area, It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task DeleteFeature_NotFound_Returns404()
    {
        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory: factory);
            var result = await ctrl.DeleteFeature(Guid.NewGuid());
            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(404, objResult.StatusCode);
        }
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

        var (db, factory) = CreateInMemoryDbPair("FeaturesTest");
        await using (db)
        {
            var ctrl = CreateController(db, featureStatsService: statsMock.Object, factory: factory);

            var result = await ctrl.GetStats();

            var ok = Assert.IsType<OkObjectResult>(result);
            var stats = Assert.IsType<FeatureStatsResponse>(ok.Value);
            Assert.Equal(2, stats.Area);
            Assert.Equal(3, stats.Road);
            Assert.Equal(5, stats.Total);
        }
    }

}
