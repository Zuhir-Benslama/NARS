using System.Text.Json.Nodes;
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

namespace NarsApi.Tests.Integration;

[Collection(PostgreSqlCollection.CollectionName)]
public class FieldControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private AppDbContext _db = null!;
    private Guid _workerId;

    public FieldControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        _workerId = await CreateWorkerAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _fixture.CleanTablesAsync();
    }

    private FieldController CreateController()
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var fieldSvc = new FieldService(_db, Mock.Of<ILogger<FieldService>>());
        var ctrl = new FieldController(_db, Mock.Of<ILogger<FieldController>>(), Options.Create(new FeatureDefaultsOptions()), timeProvider, fieldSvc, Mock.Of<IWebHostEnvironment>());
        var httpContext = new DefaultHttpContext
        {
            User = AuthTestHelper.CreateClaimsPrincipal(_workerId, UserRoles.FieldWorker, communeId: 1)
        };
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };
        return ctrl;
    }

    private async Task<Guid> CreateWorkerAsync()
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.FieldWorker, communeId: 1, name: "Field Worker Integration");
        await SeedData.SeedBasicLocationsAsync(_db);
        return user.Id;
    }

    private async Task<Guid> CreateRoadWithOwnerAsync()
    {
        var ownerId = Guid.NewGuid();
        await _db.Users.AddAsync(new User
        {
            Id = ownerId,
            Name = "Road Owner",
            Email = $"road-owner-{ownerId:N}@test.com",
            Phone = DefaultPhone,
            Username = $"road_owner_{ownerId:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });

        var roadId = Guid.NewGuid();
        _db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = ownerId,
            Data = """{"coordinates":[{"lat":36.71,"lng":2.95},{"lat":36.72,"lng":2.96}]}""",
            Label = "Integration Test Road",
            Layer = "street",
            UpdatedAt = FixedUtcNow,
        });
        _db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });

        await _db.SaveChangesAsync();
        return roadId;
    }

    [Fact]
    public async Task GetFeatures_ValidType_ReturnsFeatures()
    {
        var controller = CreateController();
        await CreateRoadWithOwnerAsync();

        var result = await controller.GetFeatures(type: "road");

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<LoadFeaturesResponse<FieldFeatureResult>>(ok.Value);
        Assert.NotEmpty(resp.Features);
    }

    [Fact]
    public async Task GetFeatures_NoType_Returns400()
    {
        var controller = CreateController();
        var result = await controller.GetFeatures(type: null);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_ValidRoad_Returns201()
    {
        var controller = CreateController();
        var roadId = await CreateRoadWithOwnerAsync();

        var result = await controller.SubmitInspection(new FieldInspectRequest(
            FeatureId: roadId.ToString(),
            Type: FeatureTypes.Road,
            Data: JsonNode.Parse("{}")!,
            Status: "good"
        ));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var resp = Assert.IsType<FieldInspectSubmitResponse>(created.Value);
        Assert.True(resp.Success);
    }

    [Fact]
    public async Task SubmitInspection_InvalidType_Returns400()
    {
        var controller = CreateController();
        var result = await controller.SubmitInspection(new FieldInspectRequest(
            FeatureId: Guid.NewGuid().ToString(),
            Type: "invalid_type",
            Data: JsonNode.Parse("{}")!,
            Status: "good"
        ));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_WrongCommune_Returns403()
    {
        var controller = CreateController();
        // Create a user in a different commune
        var otherOwnerId = Guid.NewGuid();
        await _db.Users.AddAsync(new User
        {
            Id = otherOwnerId,
            Name = "Other Commune Owner",
            Email = $"other-{otherOwnerId:N}@test.com",
            Phone = DefaultPhone,
            Username = $"other_owner_{otherOwnerId:N}",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role = UserRoles.CommuneUser,
            CommuneId = 99,
        });

        var roadId = Guid.NewGuid();
        _db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = otherOwnerId,
            Data = "{}",
            Label = "Other Commune Road",
            Layer = "street",
            UpdatedAt = FixedUtcNow,
        });
        _db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });
        await _db.SaveChangesAsync();

        var result = await controller.SubmitInspection(new FieldInspectRequest(
            FeatureId: roadId.ToString(),
            Type: FeatureTypes.Road,
            Data: JsonNode.Parse("{}")!,
            Status: "good"
        ));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetInspections_ReturnsInspections()
    {
        var controller = CreateController();
        var roadId = await CreateRoadWithOwnerAsync();

        await controller.SubmitInspection(new FieldInspectRequest(
            FeatureId: roadId.ToString(),
            Type: FeatureTypes.Road,
            Data: JsonNode.Parse("{}")!,
            Status: "good"
        ));

        var result = await controller.GetInspections(roadId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<FieldInspectionsResponse>(ok.Value);
        Assert.NotEmpty(resp.Inspections);
    }

    [Fact]
    public async Task CreateEntrance_ValidRequest_Returns201()
    {
        var controller = CreateController();
        var roadId = await CreateRoadWithOwnerAsync();

        var result = await controller.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: roadId.ToString(),
            Data: JsonNode.Parse("{}")!,
            Label: "Integration Entrance"
        ));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var resp = Assert.IsType<CreateEntranceResponse>(created.Value);
        Assert.True(resp.Success);

        // Verify in DB
        var entrance = await _db.HouseEntrances.FirstOrDefaultAsync(e => e.Id == Guid.Parse(resp.Id));
        Assert.NotNull(entrance);
        Assert.Equal("Integration Entrance", entrance.Label);
    }

    [Fact]
    public async Task CreateEntrance_RoadNotFound_Returns400()
    {
        var controller = CreateController();
        var result = await controller.CreateEntranceFromInspection(new FieldEntranceCreateRequest(
            RoadId: Guid.NewGuid().ToString(),
            Data: JsonNode.Parse("{}")!,
            Label: "No Road"
        ));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task CreateEntrance_NullBody_Returns400()
    {
        var controller = CreateController();
        var result = await controller.CreateEntranceFromInspection(null!);
        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }
}
