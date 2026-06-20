using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Moq;
using Xunit;

namespace NarsApi.Tests.Integration;

/// <summary>
/// Integration tests for FeaturesController against real PostgreSQL + PostGIS.
/// Tests the full CRUD pipeline with spatial data.
/// </summary>
[Collection("PostgreSQL Integration")]
public class FeaturesControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private readonly AppDbContext _db;
    private readonly FeaturesController _controller;
    private Guid _userId;

    public FeaturesControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
        _db = fixture.CreateDbContext();

        var config = CreateConfigMock();
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == DateTime.UtcNow);
        var jwt = new JwtService("integration-test-secret-key-that-is-32chars!!", null, null, config.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<JwtService>>(), timeProvider);
        var scatteredMock = new Mock<IScatteredAreaService>();
        scatteredMock.Setup(s => s.RefreshAsync(It.IsAny<Guid>(), It.IsAny<int>())).Returns(Task.CompletedTask);
        var bgQueueMock = Mock.Of<IBackgroundTaskQueue>();

        _controller = new FeaturesController(new FeatureRepository(_db), scatteredMock.Object, bgQueueMock, Mock.Of<Microsoft.Extensions.Logging.ILogger<FeaturesController>>(), config.Object, timeProvider, new FeatureStatsService(_db));
    }

    public async Task InitializeAsync()
    {
        _userId = await CreateUserAsync();
        SetUserId(_userId, 1);
    }

    public async Task DisposeAsync()
    {
        await _fixture.CleanTablesAsync();
    }

    [Fact]
    public async Task SaveFeature_ValidArea_Returns201()
    {
        var data = new
        {
            coordinates = new[] {
                new { lat = 36.71, lng = 2.95 },
                new { lat = 36.73, lng = 2.95 },
                new { lat = 36.73, lng = 2.97 },
                new { lat = 36.71, lng = 2.97 },
            }
        };

        var result = await _controller.SaveFeature(new FeatureSaveRequest(
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
        var data = new
        {
            coordinates = new[] {
                new { lat = 36.71, lng = 2.95 },
                new { lat = 36.72, lng = 2.96 },
            }
        };

        var result = await _controller.SaveFeature(new FeatureSaveRequest(
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
        var result = await _controller.SaveFeature(new FeatureSaveRequest(
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
        // Create some features first
        var dataArea = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };
        var dataRoad = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 } } };

        await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "central_urban", Label: "Area 1",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dataArea)).RootElement));

        await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "road", Layer: "street", Label: "Road 1",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(dataRoad)).RootElement));

        var result = await _controller.LoadFeatures();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var loadResponse = Assert.IsType<LoadFeaturesResponse>(okResult.Value);
        Assert.True(loadResponse.Count >= 2);
    }

    [Fact]
    public async Task DeleteFeature_RemovesFromDatabase()
    {
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        var saveResult = await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "central_urban", Label: "To Delete",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        // The response is an anonymous type — extract the ID via reflection
        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        Assert.Equal(201, saveOk.StatusCode);
        var saveResponse = Assert.IsType<SaveFeatureResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        // Delete it
        var deleteResult = await _controller.DeleteFeature(featureId);
        var deleteOk = Assert.IsType<OkObjectResult>(deleteResult);
        Assert.Equal(200, deleteOk.StatusCode);

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
        var result = await _controller.DeleteFeature(Guid.NewGuid());
        var notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task ClearFeatures_RemovesAll()
    {
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "central_urban", Label: "Area 1",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));
        await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "secondary_urban", Label: "Area 2",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        var result = await _controller.ClearFeatures(new ClearFeaturesRequest(Confirm: true));
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
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 }, new { lat = 36.71, lng = 2.96 } } };

        var saveResult = await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "area", Layer: "central_urban", Label: "Original Label",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        var saveResponse = Assert.IsType<SaveFeatureResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        // Update the label
        var updateData = new { coordinates = new[] { new { lat = 36.80, lng = 3.00 } } };
        var updateResult = await _controller.UpdateFeature(featureId, new FeatureUpdateRequest(
            Label: "Updated Label",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(updateData)).RootElement
        ));

        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal(200, updateOk.StatusCode);

        // Verify the update persisted
        var area = await _db.Areas.AsNoTracking().FirstOrDefaultAsync(a => a.Id == featureId);
        Assert.NotNull(area);
        Assert.Equal("Updated Label", area.Label);
        Assert.Contains("36.8", area.Data);
    }

    [Fact]
    public async Task UpdateFeature_NonExistent_Returns404()
    {
        var result = await _controller.UpdateFeature(Guid.NewGuid(), new FeatureUpdateRequest(Label: "Test", Data: null));
        var notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    [Fact]
    public async Task UpdateFeature_LabelOnly_Returns200()
    {
        var data = new { coordinates = new[] { new { lat = 36.71, lng = 2.95 }, new { lat = 36.72, lng = 2.96 } } };

        var saveResult = await _controller.SaveFeature(new FeatureSaveRequest(
            Type: "road", Layer: "street", Label: "Road Before",
            Data: System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data)).RootElement));

        var saveOk = Assert.IsType<ObjectResult>(saveResult);
        var saveResponse = Assert.IsType<SaveFeatureResponse>(saveOk.Value);
        var featureId = Guid.Parse(saveResponse.Id);

        var updateResult = await _controller.UpdateFeature(featureId, new FeatureUpdateRequest(Label: "Road After", Data: null));
        var updateOk = Assert.IsType<OkObjectResult>(updateResult);
        Assert.Equal(200, updateOk.StatusCode);

        var road = await _db.Roads.AsNoTracking().FirstOrDefaultAsync(r => r.Id == featureId);
        Assert.Equal("Road After", road!.Label);
    }

    private async Task<Guid> CreateUserAsync()
    {
        var userId = Guid.NewGuid();
        await _db.Users.AddAsync(new User
        {
            Id = userId,
            Name = "Features Test User",
            Email = $"features-{userId:N}@test.com",
            Phone = "0555000000",
            Username = $"features_{userId:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            CommuneId = 1,
        });

        // Seed reference data
        if (!await _db.Communes.AnyAsync())
        {
            await _db.Wilayas.AddAsync(new Wilaya { WilayaId = 1, WilayaFr = "Alger" });
            await _db.Dairas.AddAsync(new Daira { DairaId = 1, WilayaId = 1, DairaFr = "Draria" });
            await _db.Communes.AddAsync(new Commune { CommuneId = 1, DairaId = 1, CommuneFr = "Draria Centre" });

            var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
            var boundary = factory.CreatePolygon(new[] {
                new NetTopologySuite.Geometries.Coordinate(2.95, 36.71),
                new NetTopologySuite.Geometries.Coordinate(2.97, 36.71),
                new NetTopologySuite.Geometries.Coordinate(2.97, 36.73),
                new NetTopologySuite.Geometries.Coordinate(2.95, 36.73),
                new NetTopologySuite.Geometries.Coordinate(2.95, 36.71),
            });
            await _db.CommuneBoundaries.AddAsync(new CommuneBoundary { CommuneId = 1, Geometry = boundary });
        }

        await _db.SaveChangesAsync();
        return userId;
    }

    private void SetUserId(Guid userId, int communeId)
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("user_id", userId.ToString()),
             new System.Security.Claims.Claim("commune_id", communeId.ToString())],
            "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);
        _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };
    }

    private static Mock<IConfiguration> CreateConfigMock()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Jwt:SecretKey"]).Returns("test-secret-key-that-is-at-least-32-chars!!");
        config.Setup(c => c["Jwt:ExpiresInMinutes"]).Returns("60");
        config.Setup(c => c["Jwt:RefreshExpiresInDays"]).Returns("30");
        return config;
    }
}
