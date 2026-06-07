using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using Xunit;

namespace NarsApi.Tests.Integration;

/// <summary>
/// Integration tests for ValidationController against real PostgreSQL + PostGIS.
/// Tests spatial validation logic that cannot be tested with InMemory provider.
/// </summary>
[Collection("PostgreSQL Integration")]
public class ValidationControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private readonly AppDbContext _db;
    private readonly ValidationController _controller;

    public ValidationControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
        _db = fixture.CreateDbContext();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Validation:MaxCoordinateCount"] = "10000",
            ["Validation:RoadTurnAngleDegrees"] = "90",
            ["Validation:RoadConnectivityMeters"] = "20",
            ["Validation:DistrictBoundaryToleranceMeters"] = "10",
        }).Build();
        _controller = new ValidationController(_db, config);
    }

    public async Task InitializeAsync()
    {
        await SeedReferenceDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.CleanTablesAsync();
    }

    [Fact]
    public async Task ValidateRoad_MustHaveAtLeast2Points()
    {
        SetUserId(await CreateTestUserAsync());

        var result = await _controller.ValidateRoad(new ValidateRoadRequest(
            Coordinates: [new CoordDto(3.0, 36.0)]
        ));

        var badResult = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ValidateRoadResponse>(badResult.Value);
        Assert.Contains("at least 2 points", response.Error!);
    }

    [Fact]
    public async Task ValidateDistrict_MustHaveAtLeast3Points()
    {
        SetUserId(await CreateTestUserAsync());

        var result = await _controller.ValidateDistrict(new ValidateDistrictRequest(
            Coordinates: [
                new CoordDto(3.0, 36.0),
                new CoordDto(3.1, 36.0),
            ],
            DistrictTypeKey: "district"
        ));

        var badResult = Assert.IsType<BadRequestObjectResult>(result);
        var response = Assert.IsType<ValidateDistrictResponse>(badResult.Value);
        Assert.Contains("at least 3 points", response.Error!);
    }

    [Fact]
    public async Task ValidateDistrict_OverlappingPolygon_ReturnsError()
    {
        var userId = await CreateTestUserAsync();
        SetUserId(userId);

        // Create a district in the database — a 0.02° x 0.02° square
        // Coordinates stored as {lat, lng} objects
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
            Layer = "district",
            Label = "Existing District",
            Data = existingData,
        });
        await _db.SaveChangesAsync();

        // Try to create a district that significantly overlaps the existing one
        var result = await _controller.ValidateDistrict(new ValidateDistrictRequest(
            Coordinates: [
                new CoordDto(36.715, 2.955),
                new CoordDto(36.725, 2.955),
                new CoordDto(36.725, 2.965),
                new CoordDto(36.715, 2.965),
            ],
            DistrictTypeKey: "district"
        ));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ValidateDistrictResponse>(okResult.Value);
        Assert.False(response.Valid);
        Assert.Contains("overlap", response.Error!.ToLower());
    }

    private async Task<Guid> CreateTestUserAsync()
    {
        var userId = Guid.NewGuid();
        await _db.Users.AddAsync(new User
        {
            Id = userId,
            Name = "Validation Test User",
            Email = $"validation-{userId:N}@test.com",
            Phone = "0555000000",
            Username = $"validation_{userId:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            CommuneId = 1,
        });
        await _db.SaveChangesAsync();
        return userId;
    }

    private async Task SeedReferenceDataAsync()
    {
        if (await _db.Communes.AnyAsync()) return;

        await _db.Wilayas.AddAsync(new Wilaya { WilayaId = 1, WilayaFr = "Alger" });
        await _db.Dairas.AddAsync(new Daira { DairaId = 1, WilayaId = 1, DairaFr = "Draria" });
        await _db.Communes.AddAsync(new Commune { CommuneId = 1, DairaId = 1, CommuneFr = "Draria Centre" });

        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var boundary = factory.CreatePolygon(new[] {
            new NetTopologySuite.Geometries.Coordinate(2.90, 36.70),
            new NetTopologySuite.Geometries.Coordinate(3.00, 36.70),
            new NetTopologySuite.Geometries.Coordinate(3.00, 36.80),
            new NetTopologySuite.Geometries.Coordinate(2.90, 36.80),
            new NetTopologySuite.Geometries.Coordinate(2.90, 36.70),
        });
        await _db.CommuneBoundaries.AddAsync(new CommuneBoundary { CommuneId = 1, Geometry = boundary });
        await _db.SaveChangesAsync();
    }

    private void SetUserId(Guid userId)
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim("user_id", userId.ToString())],
            "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);
        _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };
    }
}
