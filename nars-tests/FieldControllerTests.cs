using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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

public class FieldControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly DateTime FixedNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"FieldTest_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

    private static FieldController CreateController(
        AppDbContext db,
        IFieldService? fieldService = null,
        IDateTimeProvider? timeProvider = null)
    {
        return new FieldController(
            db,
            Mock.Of<ILogger<FieldController>>(),
            Options.Create(new FeatureDefaultsOptions()),
            timeProvider ?? Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedNow),
            fieldService ?? Mock.Of<IFieldService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static void SetUser(FieldController ctrl, string role,
        int? communeId = null, string? username = null)
    {
        ctrl.ControllerContext.HttpContext.User =
            AuthTestHelper.CreateClaimsPrincipal(UserId, role, communeId, username: username ?? "fieldworker");
    }

    private static JsonNode Json(string raw) => JsonNode.Parse(raw)!;

    // ─── GET /api/field/features ─────────────────────────────────────────

    [Fact]
    public async Task GetFeatures_NonFieldWorker_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.GetFeatures(type: "road");

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetFeatures_NoCommuneId_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker);

        var result = await ctrl.GetFeatures(type: "road");

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task GetFeatures_NullType_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetFeatures(type: null);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task GetFeatures_InvalidType_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetFeatures(type: "invalid_type");

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task GetFeatures_ValidRequest_ReturnsOkWithFeatures()
    {
        var db = CreateDb();
        var mockFieldService = new Mock<IFieldService>();
        var descriptor = FeatureTypeRegistry.GetDescriptor(FeatureTypes.Road)!;
        mockFieldService.Setup(s => s.QueryFeaturesAsync(descriptor, 1, 0, 500, default))
            .ReturnsAsync((new List<FieldFeatureResult>
            {
                new("r1", UserId.ToString(), "street", "Road 1", System.Text.Json.JsonDocument.Parse("{}").RootElement, FixedNow, null),
            }, 1));
        var ctrl = CreateController(db, fieldService: mockFieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetFeatures(type: "road");

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<LoadFeaturesResponse<FieldFeatureResult>>(ok.Value);
        Assert.Single(response.Features);
        Assert.Equal(1, response.Count);
    }

    // ─── POST /api/field/inspect ─────────────────────────────────────────

    [Fact]
    public async Task SubmitInspection_NullBody_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(null!);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_NonFieldWorker_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest("id", "road", Json("{}"), "good"));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SubmitInspection_InvalidFeatureId_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest("not-a-guid", "road", Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_InvalidType_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(Guid.NewGuid().ToString(), "area", Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_FeatureNotFound_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(Guid.NewGuid().ToString(), "road", Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_WrongCommune_ReturnsForbid()
    {
        var db = CreateDb();
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)2));
        var ctrl = CreateController(db, fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), "road", Json("{}"), "good"));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SubmitInspection_ValidRequest_Returns201()
    {
        var db = CreateDb();
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)1));
        var ctrl = CreateController(db, fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), "road", Json("""{"key": "val"}"""), "good"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var response = Assert.IsType<FieldInspectSubmitResponse>(created.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task SubmitInspection_InvalidStatus_Returns400()
    {
        var db = CreateDb();
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)1));
        var ctrl = CreateController(db, fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        db.FeatureRegistry.Add(new FeatureRegistry { Id = roadId, FeatureType = FeatureTypes.Road });
        await db.SaveChangesAsync();

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), "road", Json("{}"), "invalid_status"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    // ─── GET /api/field/inspections/{featureId} ──────────────────────────

    [Fact]
    public async Task GetInspections_NonFieldWorker_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.GetInspections(Guid.NewGuid());

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetInspections_ValidRequest_ReturnsInspections()
    {
        var db = CreateDb();
        var featureId = Guid.NewGuid();
        db.Inspections.Add(new Inspection
        {
            Id = Guid.NewGuid(),
            FeatureId = featureId,
            UserId = UserId,
            Type = FeatureTypes.Road,
            Data = "{}",
            Status = "good",
            CreatedAt = FixedNow,
        });
        db.Inspections.Add(new Inspection
        {
            Id = Guid.NewGuid(),
            FeatureId = featureId,
            UserId = UserId,
            Type = FeatureTypes.Road,
            Data = "{}",
            Status = "issue",
            CreatedAt = FixedNow.AddHours(1),
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetInspections(featureId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FieldInspectionsResponse>(ok.Value);
        Assert.Equal(2, response.Inspections.Count);
    }

    [Fact]
    public async Task GetInspections_NoInspections_ReturnsEmptyList()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetInspections(Guid.NewGuid());

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FieldInspectionsResponse>(ok.Value);
        Assert.Empty(response.Inspections);
    }

    // ─── POST /api/field/entrance ────────────────────────────────────────

    [Fact]
    public async Task CreateEntranceFromInspection_NullBody_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(null!);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_NonFieldWorker_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest("road-id", Json("{}"), "Label"));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_InvalidRoadId_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest("not-a-guid", Json("{}"), "Label"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_RoadNotFound_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(Guid.NewGuid().ToString(), Json("{}"), "Label"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_ValidRequest_Returns201()
    {
        var db = CreateDb();
        var userId = UserId;
        db.Users.Add(new User
        {
            Id = userId,
            Username = "owner",
            Name = "Owner",
            Email = "owner@test.com",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = "commune_user",
            CommuneId = 1,
        });
        var roadId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = userId,
            Layer = "street",
            Label = "Main Street",
            Data = "{}",
            CreatedAt = FixedNow,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(roadId.ToString(), Json("""{"coordinates": [[36.0, 3.0]]}"""), "Entrance Label"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var response = Assert.IsType<CreateEntranceResponse>(created.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_DifferentCommune_ReturnsForbid()
    {
        var db = CreateDb();
        var userId = UserId;
        db.Users.Add(new User
        {
            Id = userId,
            Username = "owner",
            Name = "Owner",
            Email = "owner@test.com",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = "commune_user",
            CommuneId = 2,
        });
        var roadId = Guid.NewGuid();
        db.Roads.Add(new Road
        {
            Id = roadId,
            UserId = userId,
            Layer = "street",
            Label = "Main Street",
            Data = "{}",
            CreatedAt = FixedNow,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(roadId.ToString(), Json("{}"), "Label"));

        Assert.IsType<ForbidResult>(result);
    }
}
