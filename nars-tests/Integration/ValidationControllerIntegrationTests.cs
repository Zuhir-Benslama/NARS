using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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

namespace NarsApi.Tests.Integration;

/// <summary>
/// Integration tests for ValidationController against real PostgreSQL + PostGIS.
/// Tests spatial validation logic that cannot be tested with InMemory provider.
/// </summary>
[Collection("PostgreSQL Integration")]
public class ValidationControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private AppDbContext _db = null!;

    public ValidationControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        await SeedReferenceDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _fixture.CleanTablesAsync();
    }

    private ValidationController CreateController(Guid userId)
    {
        var ctrl = new ValidationController(
            Options.Create(new ValidationOptions()),
            new ValidationService(_db),
            Mock.Of<ILogger<ValidationController>>(),
            Mock.Of<IWebHostEnvironment>());
        var httpContext = new DefaultHttpContext
        {
            User = AuthTestHelper.CreateClaimsPrincipal(userId, "commune_user", communeId: 1)
        };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
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
            DistrictTypeKey: "district"
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
            Layer = "district",
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
            DistrictTypeKey: "district"
        ));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ValidateDistrictResponse>(okResult.Value);
        Assert.False(response.Valid);
        Assert.False(string.IsNullOrEmpty(response.Error));
    }

    private async Task<Guid> CreateTestUserAsync()
    {
        var user = await SeedData.CreateUserAsync(_db, "commune_user", communeId: 1, name: "Validation Test User");
        return user.Id;
    }

    private async Task SeedReferenceDataAsync() => await SeedData.SeedBasicLocationsAsync(_db);
}
