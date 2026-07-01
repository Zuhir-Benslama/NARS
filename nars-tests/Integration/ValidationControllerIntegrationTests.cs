using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
        _controller = new ValidationController(_db, Options.Create(new ValidationOptions()), new ValidationService(_db));
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

        var badResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, badResult.StatusCode);
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

        var badResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, badResult.StatusCode);
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
            Phone = DefaultPhone,
            Username = $"validation_{userId:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            CommuneId = 1,
        });
        await _db.SaveChangesAsync();
        return userId;
    }

    private async Task SeedReferenceDataAsync()
    {
        await SeedData.SeedBasicLocationsAsync(_db);
    }

    private void SetUserId(Guid userId)
    {
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        var identity = new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(ClaimNames.UserId, userId.ToString())],
            "TestAuth");
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(identity);
        _controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContext };
    }
}
