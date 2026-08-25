using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Moq;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Integration tests for FeaturesController against real PostgreSQL + PostGIS.
/// Tests the full CRUD pipeline with spatial data.
/// </summary>
[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class FeaturesControllerServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
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

    private FeaturesController CreateController()
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var bgQueueMock = Mock.Of<IBackgroundTaskQueue>();
        var factory = _fixture.CreateDbContextFactory();
        var ctrl = new FeaturesController(
            new FeatureService(factory, bgQueueMock, new FeatureCleanupService(), Mock.Of<ILogger<FeatureService>>()),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider,
            new FeatureStatsService(_fixture.CreateDbContextFactory()),
            Mock.Of<ILogger<FeaturesController>>(),
            Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, _userId, UserRoles.CommuneUser, communeId: 1);
        return ctrl;
    }

    [Fact]
    public async Task SaveFeature_ValidArea_Returns201()
    {
        var controller = CreateController();
        var data = new
        {
            coordinates = new[] {
                new { lat = 36.71, lng = 2.95 },
                new { lat = 36.73, lng = 2.95 },
                new { lat = 36.73, lng = 2.97 },
                new { lat = 36.71, lng = 2.97 },
            }
        };

        var result = await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area,
            Layer: FeatureTypes.AreaLayers.CentralUrban,
            Label: "Test Central Urban",
            Data: ToJsonElement(data)
        ));

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);

        // Verify in database
        var area = await _db.Areas.FirstOrDefaultAsync(a => a.UserId == _userId && a.Label == "Test Central Urban");
        Assert.NotNull(area);
        Assert.Equal(FeatureTypes.AreaLayers.CentralUrban, area.Layer);
    }

    [Fact]
    public async Task SaveFeature_ValidRoad_Returns201()
    {
        var controller = CreateController();
        var data = new
        {
            coordinates = new[] {
                new { lat = 36.71, lng = 2.95 },
                new { lat = 36.72, lng = 2.96 },
            }
        };

        var result = await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Road,
            Layer: FeatureTypes.RoadLayers.Street,
            Label: "Test Road",
            Data: ToJsonElement(data)
        ));

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);

        var road = await _db.Roads.FirstOrDefaultAsync(r => r.UserId == _userId && r.Label == "Test Road");
        Assert.NotNull(road);
    }

    [Fact]
    public async Task SaveFeature_InvalidType_Returns400()
    {
        var controller = CreateController();
        var result = await controller.SaveFeature(new FeatureSaveRequest(
            Type: "nonexistent_type",
            Layer: FeatureTypes.AreaLayers.CentralUrban,
            Label: "Bad Type",
            Data: ToJsonElement("{}")
        ));

        var badRequest = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, badRequest.StatusCode);
    }

    [Fact]
    public async Task LoadFeatures_ReturnsAllUserFeatures()
    {
        var controller = CreateController();
        // Create some features first
        var dataArea = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };
        var dataRoad = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 } } };

        await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area, Layer: FeatureTypes.AreaLayers.CentralUrban, Label: "Area 1",
            Data: ToJsonElement(dataArea)));

        await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Road, Layer: FeatureTypes.RoadLayers.Street, Label: "Road 1",
            Data: ToJsonElement(dataRoad)));

        var result = await controller.LoadFeatures();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var loadResponse = Assert.IsType<LoadFeaturesResponse<FeatureResult>>(okResult.Value);
        Assert.Equal(2, loadResponse.Count);
        Assert.Equal(2, loadResponse.Features.Count);
        Assert.Contains(loadResponse.Features, f => f.Type == FeatureTypes.Area && f.Label == "Area 1");
        Assert.Contains(loadResponse.Features, f => f.Type == FeatureTypes.Road && f.Label == "Road 1");
    }

    [Fact]
    public async Task LoadFeatures_OffsetPastLastRow_StillReportsTotalCount()
    {
        var controller = CreateController();
        var dataArea = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area, Layer: FeatureTypes.AreaLayers.CentralUrban, Label: "Area 1",
            Data: ToJsonElement(dataArea)));

        // A page that starts past the last row must report the true total (1),
        // not 0 — clients paginating by Count would otherwise stop early.
        var result = await controller.LoadFeatures(skip: 10);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var loadResponse = Assert.IsType<LoadFeaturesResponse<FeatureResult>>(okResult.Value);
        Assert.Empty(loadResponse.Features);
        Assert.Equal(1, loadResponse.Count);
    }

    [Fact]
    public async Task DeleteFeature_RemovesFromDatabase()
    {
        var controller = CreateController();
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        var saveResult = await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area, Layer: FeatureTypes.AreaLayers.CentralUrban, Label: "To Delete",
            Data: ToJsonElement(data)));

        // Extract the ID from the response
        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        Assert.Equal(201, saveOk.StatusCode);
        var saveResponse = Assert.IsType<CreateResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        // Delete it
        var deleteResult = await controller.DeleteFeature(featureId);
        Assert.IsType<NoContentResult>(deleteResult);

        // Verify it's gone (use AsNoTracking to avoid change tracker caching)
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == featureId);
        Assert.Null(area);

        // Verify registry is also cleaned up
        var reg = await _db.FeatureRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.Id == featureId);
        Assert.Null(reg);
    }

    [Fact]
    public async Task DeleteFeature_NonExistent_Returns404()
    {
        var controller = CreateController();
        var result = await controller.DeleteFeature(Guid.NewGuid());
        var notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task DeleteRoad_RemovesOrphanedHouseEntrances()
    {
        var controller = CreateController();

        // Save a road feature.
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 } } };
        var saveResult = await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Road, Layer: FeatureTypes.RoadLayers.Street, Label: "Road A",
            Data: ToJsonElement(data)));
        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        Assert.Equal(201, saveOk.StatusCode);
        var saveResponse = Assert.IsType<CreateResponse>(saveOk.Value);
        var roadId = Guid.Parse(saveResponse.Id);

        // Attach a house entrance owned by that road.
        var fieldService = new FieldService(
            _fixture.CreateDbContextFactory(),
            Mock.Of<IFeatureService>(),
            Mock.Of<ILogger<FieldService>>());
        var entranceId = await fieldService.CreateEntranceAsync(roadId, _userId, _userId, "Entrance A", "{}");

        // Delete the road — the orphaned entrance must go with it.
        var deleteResult = await controller.DeleteFeature(roadId);
        Assert.IsType<NoContentResult>(deleteResult);

        var entrance = await _db.HouseEntrances.AsNoTracking().FirstOrDefaultAsync(e => e.Id == entranceId);
        Assert.Null(entrance);
        var reg = await _db.FeatureRegistry.AsNoTracking().FirstOrDefaultAsync(r => r.Id == entranceId);
        Assert.Null(reg);
    }

    [Fact]
    public async Task ClearFeatures_RemovesAll()
    {
        var controller = CreateController();
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area, Layer: FeatureTypes.AreaLayers.CentralUrban, Label: "Area 1",
            Data: ToJsonElement(data)));
        await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area, Layer: FeatureTypes.AreaLayers.SecondaryUrban, Label: "Area 2",
            Data: ToJsonElement(data)));

        var result = await controller.ClearFeatures(new ClearFeaturesRequest(Confirm: true));
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        // Verify all features are gone
        var areaCount = await _db.Areas.CountAsync(a => a.UserId == _userId);
        Assert.Equal(0, areaCount);
        var regCount = await _db.FeatureRegistry.CountAsync();
        Assert.Equal(0, regCount);
    }

    [Fact]
    public async Task UpdateFeature_ValidUpdate_Returns200()
    {
        var controller = CreateController();
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        var saveResult = await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Area, Layer: FeatureTypes.AreaLayers.CentralUrban, Label: "Original Label",
            Data: ToJsonElement(data)));

        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        var saveResponse = Assert.IsType<CreateResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        // Update the label
        var updateData = new { coordinates = new[] { new { lat = 36.80, lng = 3.00 } } };
        var updateResult = await controller.UpdateFeature(featureId, new FeatureUpdateRequest(
            Label: "Updated Label",
            Data: ToJsonElement(updateData)
        ));

        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal(200, updateOk.StatusCode);

        // Verify the update persisted
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == featureId);
        Assert.NotNull(area);
        Assert.Equal("Updated Label", area.Label);
        using var persistedData = System.Text.Json.JsonDocument.Parse(area.Data);
        var coords = persistedData.RootElement.GetProperty("coordinates")[0];
        Assert.Equal(36.80, coords.GetProperty("lat").GetDouble(), 4);
        Assert.Equal(3.00, coords.GetProperty("lng").GetDouble(), 4);
    }

    [Fact]
    public async Task UpdateFeature_NonExistent_Returns404()
    {
        var controller = CreateController();
        var result = await controller.UpdateFeature(Guid.NewGuid(), new FeatureUpdateRequest(Label: "Test", Data: null));
        var notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_LabelOnly_Returns200()
    {
        var controller = CreateController();
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 } } };

        var saveResult = await controller.SaveFeature(new FeatureSaveRequest(
            Type: FeatureTypes.Road, Layer: FeatureTypes.RoadLayers.Street, Label: "Road Before",
            Data: ToJsonElement(data)));

        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        var saveResponse = Assert.IsType<CreateResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        var updateResult = await controller.UpdateFeature(featureId, new FeatureUpdateRequest(Label: "Road After", Data: null));
        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal(200, updateOk.StatusCode);

        var road = await _db.Roads.AsNoTracking().FirstOrDefaultAsync(r => r.Id == featureId);
        Assert.Equal("Road After", road!.Label);
    }

    private async Task<Guid> CreateUserAsync()
    {
        await SeedData.SeedBasicLocationsAsync(_db);
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1, name: "Features Test User");
        return user.Id;
    }

}
