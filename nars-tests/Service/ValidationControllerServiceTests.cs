using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.Infrastructure;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

/// <summary>
/// Integration tests for ValidationController against real PostgreSQL + PostGIS.
/// Tests spatial validation logic that cannot be tested with InMemory provider.
/// </summary>
[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class ValidationControllerServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture = fixture;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        await SeedReferenceDataAsync();
    }

    public async Task DisposeAsync()
    {
        try { await _db.DisposeAsync(); }
        finally { await _fixture.CleanTablesAsync(); }
    }

    private ValidationController CreateController(Guid userId)
    {
        var ctrl = new ValidationController(
            Options.Create(new ValidationOptions()),
            new ValidationService(_fixture.CreateDbContextFactory()),
            Mock.Of<ILogger<ValidationController>>(),
            Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, userId, UserRoles.CommuneUser, communeId: 1);
        return ctrl;
    }

    [Fact]
    public async Task ValidateRoad_MustHaveAtLeast2Points()
    {
        var controller = CreateController(await CreateTestUserAsync());

        var result = await controller.ValidateRoad(new ValidateRoadRequest(
            Coordinates: [new CoordDto(3.0, 36.0)]
        ));

        var badResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, badResult.StatusCode);
    }

    [Fact]
    public async Task ValidateDistrict_MustHaveAtLeast3Points()
    {
        var controller = CreateController(await CreateTestUserAsync());

        var result = await controller.ValidateDistrict(new ValidateDistrictRequest(
            Coordinates: [
                new CoordDto(3.0, 36.0),
                new CoordDto(3.1, 36.0),
            ],
            DistrictTypeKey: FeatureTypes.District
        ));

        var badResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, badResult.StatusCode);
    }

    [Fact]
    public async Task ValidateDistrict_OverlappingPolygon_ReturnsError()
    {
        var userId = await CreateTestUserAsync();
        var controller = CreateController(userId);

        // Create a district in the database — a 0.02° x 0.02° square
        var existingData = System.Text.Json.JsonSerializer.Serialize(new
        {
            coordinates = new[] {
                new { lat = 36.710, lng = 2.950 },
                new { lat = 36.730, lng = 2.950 },
                new { lat = 36.730, lng = 2.970 },
                new { lat = 36.710, lng = 2.970 },
            }
        });

        await _db.Districts.AddAsync(new District
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Layer = FeatureTypes.DistrictLayers.DistrictLayer,
            Label = "Existing District",
            Data = existingData,
        });
        await _db.SaveChangesAsync();

        // Try to create a district that significantly overlaps the existing one
        var result = await controller.ValidateDistrict(new ValidateDistrictRequest(
            Coordinates: [
                new CoordDto(36.715, 2.955),
                new CoordDto(36.725, 2.955),
                new CoordDto(36.725, 2.965),
                new CoordDto(36.715, 2.965),
            ],
            DistrictTypeKey: FeatureTypes.District
        ));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ValidateDistrictResponse>(okResult.Value);
        Assert.False(response.Valid);
        Assert.False(string.IsNullOrEmpty(response.Error));
    }

    [Fact]
    public async Task CheckDistrictAdjacency_TouchingWithoutSharedUrbanArea_ReturnsFalse()
    {
        // Regression test for the SQL operator-precedence bug: a district that
        // merely touches an existing district must NOT pass the adjacency check
        // unless a single urban area intersects both. Previously the expression
        // was `Touches OR (BoundaryIntersects AND EXISTS(urban area))`, so any
        // touching district short-circuited past the urban-area gate.
        var userId = await CreateTestUserAsync();
        var service = new ValidationService(_fixture.CreateDbContextFactory());

        var existingSquare = new[]
        {
            new CoordDto(36.000, 2.900),
            new CoordDto(36.020, 2.900),
            new CoordDto(36.020, 2.920),
            new CoordDto(36.000, 2.920),
        };

        await _db.Districts.AddAsync(new District
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Layer = FeatureTypes.DistrictLayers.DistrictLayer,
            Label = "Existing District",
            Data = System.Text.Json.JsonSerializer.Serialize(new { coordinates = existingSquare }),
        });
        await _db.SaveChangesAsync();

        // Shares an edge with the existing district (lat 36.020) but no urban
        // area intersects either polygon.
        var touchingSquare = new[]
        {
            new CoordDto(36.020, 2.900),
            new CoordDto(36.040, 2.900),
            new CoordDto(36.040, 2.920),
            new CoordDto(36.020, 2.920),
        };

        var wkt = GeometryHelper.BuildPolygonWkt([.. touchingSquare]);
        var adjacent = await service.CheckDistrictAdjacencyAsync(userId, wkt);

        Assert.False(adjacent);
    }

    [Fact]
    public async Task CheckDistrictAdjacency_TouchingWithinSharedUrbanArea_ReturnsTrue()
    {
        var userId = await CreateTestUserAsync();
        var service = new ValidationService(_fixture.CreateDbContextFactory());

        var urbanSquare = new[]
        {
            new CoordDto(35.98, 2.88),
            new CoordDto(36.06, 2.88),
            new CoordDto(36.06, 2.96),
            new CoordDto(35.98, 2.96),
        };

        var existingSquare = new[]
        {
            new CoordDto(36.000, 2.900),
            new CoordDto(36.020, 2.900),
            new CoordDto(36.020, 2.920),
            new CoordDto(36.000, 2.920),
        };

        await _db.Areas.AddAsync(new Area
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Layer = FeatureTypes.AreaLayers.CentralUrban,
            Label = "Urban Area",
            Data = System.Text.Json.JsonSerializer.Serialize(new { coordinates = urbanSquare }),
        });
        await _db.Districts.AddAsync(new District
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Layer = FeatureTypes.DistrictLayers.DistrictLayer,
            Label = "Existing District",
            Data = System.Text.Json.JsonSerializer.Serialize(new { coordinates = existingSquare }),
        });
        await _db.SaveChangesAsync();

        var touchingSquare = new[]
        {
            new CoordDto(36.020, 2.900),
            new CoordDto(36.040, 2.900),
            new CoordDto(36.040, 2.920),
            new CoordDto(36.020, 2.920),
        };

        var wkt = GeometryHelper.BuildPolygonWkt([.. touchingSquare]);
        var adjacent = await service.CheckDistrictAdjacencyAsync(userId, wkt);

        Assert.True(adjacent);
    }

    private async Task<Guid> CreateTestUserAsync()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: 1, name: "Validation Test User");
        return user.Id;
    }
    private async Task SeedReferenceDataAsync() => await SeedData.SeedBasicLocationsAsync(_db);
}
