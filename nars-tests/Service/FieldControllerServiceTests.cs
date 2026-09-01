using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
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

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class FieldControllerServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{
    private Guid _workerId;

    protected override async Task SeedAsync()
    {
        _workerId = await CreateWorkerAsync();
    }

    private FieldController CreateController()
    {
        var factory = Fixture.CreateDbContextFactory();
        var featureSvc = new FeatureService(factory, Mock.Of<IBackgroundTaskQueue>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<ILogger<FeatureService>>());
        var fieldSvc = new FieldService(factory, featureSvc, Mock.Of<ILogger<FieldService>>());
        var ctrl = new FieldController(Mock.Of<ILogger<FieldController>>(), Options.Create(new FeatureDefaultsOptions()), fieldSvc, Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, _workerId, UserRoles.FieldWorker, communeId: 1);
        return ctrl;
    }

    private async Task<Guid> CreateWorkerAsync()
    {
        await SeedData.SeedBasicLocationsAsync(Db);
        var user = await SeedData.CreateUserAsync(Db, UserRoles.FieldWorker, communeId: 1, name: "Field Worker Integration");
        return user.Id;
    }

    private async Task<Guid> CreateRoadWithOwnerAsync()
    {
        var owner = await SeedData.CreateUserAsync(Db, UserRoles.CommuneUser, communeId: 1, name: "Road Owner");
        var roadId = Guid.NewGuid();
        Db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = owner.Id,
            Data = """{"coordinates":[{"lat":36.71,"lng":2.95},{"lat":36.72,"lng":2.96}]}""",
            Label = "Integration Test Road",
            Layer = FeatureTypes.RoadLayers.Street,
            UpdatedAt = FixedUtcNow,
        });
        Db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });

        await Db.SaveChangesAsync();
        return roadId;
    }

    [Fact]
    public async Task GetFeatures_ValidType_ReturnsFeatures()
    {
        var controller = CreateController();
        await CreateRoadWithOwnerAsync();

        var result = await controller.GetFeatures(type: FeatureTypes.Road);

        var ok = Assert.IsType<OkObjectResult>(result);
        var resp = Assert.IsType<LoadFeaturesResponse<FieldFeatureResult>>(ok.Value);
        Assert.Single(resp.Features);
        Assert.Equal("Integration Test Road", resp.Features[0].Label);
        Assert.Equal(FeatureTypes.RoadLayers.Street, resp.Features[0].Layer);
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
        var resp = Assert.IsType<CreateResponse>(created.Value);
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

        // Seed a second commune so the road owner can be in a different one
        if (!await Db.Communes.AnyAsync(c => c.CommuneId == 2))
        {
            Db.Dairas.Add(new Daira { DairaId = 2, WilayaId = 1, DairaFr = "Bir Mourad Raïs", DairaAr = "بئر مراد رايس", DairaLatitude = 36.74, DairaLongitude = 3.05 });
            Db.Communes.Add(new Commune { CommuneId = 2, DairaId = 2, CommuneCode = 1002, CommuneFr = "Bir Mourad Raïs Centre", CommuneAr = "بئر مراد رايس الوسطى", CommuneLatitude = 36.74, CommuneLongitude = 3.05 });
            await Db.SaveChangesAsync();
        }

        // Create a user in a different commune
        var otherOwnerId = Guid.NewGuid();
        await Db.Users.AddAsync(new User
        {
            Id = otherOwnerId,
            Name = "Other Commune Owner",
            Email = $"other-{otherOwnerId:N}@test.com",
            Phone = DefaultPhone,
            Username = $"other_owner_{otherOwnerId:N}",
            PasswordHash = DefaultPasswordHash,
            SecurityStamp = User.GenerateSecurityStamp(),
            Role = UserRoles.CommuneUser,
            CommuneId = 2,
        });

        var roadId = Guid.NewGuid();
        Db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = otherOwnerId,
            Data = "{}",
            Label = "Other Commune Road",
            Layer = FeatureTypes.RoadLayers.Street,
            UpdatedAt = FixedUtcNow,
        });
        Db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });
        await Db.SaveChangesAsync();

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
        var inspection = Assert.Single(resp.Inspections);
        Assert.Equal("good", inspection.Status);
        Assert.Equal(FeatureTypes.Road, inspection.Type);
        Assert.Equal(roadId.ToString(), inspection.FeatureId);
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
        var resp = Assert.IsType<CreateResponse>(created.Value);
        Assert.True(resp.Success);

        // Verify in DB
        var entrance = await Db.HouseEntrances.FirstOrDefaultAsync(e => e.Id == Guid.Parse(resp.Id));
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
