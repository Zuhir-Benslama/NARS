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
using Microsoft.AspNetCore.Http;
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
        _db.ChangeTracker.Clear();
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var bgQueueMock = Mock.Of<IBackgroundTaskQueue>();
        var ctrl = new FeaturesController(
            new FeatureService(_db),
            bgQueueMock,
            Mock.Of<ILogger<FeaturesController>>(),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider,
            new FeatureStatsService(_fixture.CreateDbContextFactory()),
            Mock.Of<IWebHostEnvironment>());
        var httpContext = new DefaultHttpContext
        {
            User = AuthTestHelper.CreateClaimsPrincipal(_userId, UserRoles.CommuneUser, communeId: 1)
        };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
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
            Type: "area",
            Layer: "central_urban",
            Label: "Test Central Urban",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement
        ));

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);

        // Verify in database
        var area = await _db.Areas.FirstOrDefaultAsync(a => a.UserId == _userId && a.Label == "Test Central Urban");
        Assert.NotNull(area);
        Assert.Equal("central_urban", area.Layer);
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
            Type: "road",
            Layer: "street",
            Label: "Test Road",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement
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
            Layer: "central_urban",
            Label: "Bad Type",
            Data: System.Text.Json.JsonDocument.Parse("{}").RootElement
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
            Type: "area", Layer: "central_urban", Label: "Area 1",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dataArea)).RootElement));

        await controller.SaveFeature(new FeatureSaveRequest(
            Type: "road", Layer: "street", Label: "Road 1",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dataRoad)).RootElement));

        var result = await controller.LoadFeatures();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var loadResponse = Assert.IsType<LoadFeaturesResponse<FeatureResult>>(okResult.Value);
        Assert.Equal(2, loadResponse.Count);
        Assert.Equal(2, loadResponse.Features.Count);
        Assert.Contains(loadResponse.Features, f => f.Type == "area" && f.Label == "Area 1");
        Assert.Contains(loadResponse.Features, f => f.Type == "road" && f.Label == "Road 1");
    }

    [Fact]
    public async Task DeleteFeature_RemovesFromDatabase()
    {
        var controller = CreateController();
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        var saveResult = await controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "central_urban", Label: "To Delete",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        // Extract the ID from the response
        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        Assert.Equal(201, saveOk.StatusCode);
        var saveResponse = Assert.IsType<SaveFeatureResponse>(saveOk.Value);
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
    public async Task ClearFeatures_RemovesAll()
    {
        var controller = CreateController();
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        await controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "central_urban", Label: "Area 1",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));
        await controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "secondary_urban", Label: "Area 2",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

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
            Type: "area", Layer: "central_urban", Label: "Original Label",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        var saveResponse = Assert.IsType<SaveFeatureResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        // Update the label
        var updateData = new { coordinates = new[] { new { lat = 36.80, lng = 3.00 } } };
        var updateResult = await controller.UpdateFeature(featureId, new FeatureUpdateRequest(
            Label: "Updated Label",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(updateData)).RootElement
        ));

        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal(200, updateOk.StatusCode);

        // Verify the update persisted
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == featureId);
        Assert.NotNull(area);
        Assert.Equal("Updated Label", area.Label);
        var persistedData = System.Text.Json.JsonDocument.Parse(area.Data);
        var coords = persistedData.RootElement.GetProperty("coordinates")[0];
        Assert.Equal(36.80, coords.GetProperty("lat").GetDouble());
        Assert.Equal(3.00, coords.GetProperty("lng").GetDouble());
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
            Type: "road", Layer: "street", Label: "Road Before",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        var saveResponse = Assert.IsType<SaveFeatureResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        var updateResult = await controller.UpdateFeature(featureId, new FeatureUpdateRequest(Label: "Road After", Data: null));
        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal(200, updateOk.StatusCode);

        var road = await _db.Roads.AsNoTracking().FirstOrDefaultAsync(r => r.Id == featureId);
        Assert.Equal("Road After", road!.Label);
    }

    private async Task<Guid> CreateUserAsync()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1, name: "Features Test User");
        await SeedData.SeedBasicLocationsAsync(_db);
        return user.Id;
    }

}
