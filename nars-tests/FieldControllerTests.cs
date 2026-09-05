using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class FieldControllerTests
{
    private static readonly Guid OtherUserId = new("22222222-2222-2222-2222-222222222222");

    private static FieldController CreateController(
        IFieldService? fieldService = null) => new(
            Mock.Of<ILogger<FieldController>>(),
            Options.Create(new FeatureDefaultsOptions()),
            fieldService ?? Mock.Of<IFieldService>(),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static void SetUser(FieldController ctrl, string role,
        int? communeId = null, string? username = null) =>
        AuthTestHelper.SetUser(ctrl, UserId, role, communeId: communeId, username: username ?? "fieldworker");

    private static JsonNode Json(string raw) => JsonNode.Parse(raw)!;

    // ─── GET /api/field/features ─────────────────────────────────────────

    [Fact]
    public async Task GetFeatures_NoCommuneId_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker);

        var result = await ctrl.GetFeatures(type: FeatureTypes.Road);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task GetFeatures_NullType_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetFeatures(type: null);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task GetFeatures_InvalidType_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetFeatures(type: "invalid_type");

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task GetFeatures_ValidRequest_ReturnsOkWithFeatures()
    {
        var mockFieldService = new Mock<IFieldService>();
        var descriptor = FeatureTypeRegistry.GetDescriptor(FeatureTypes.Road)!;
        mockFieldService.Setup(s => s.QueryFeaturesAsync(descriptor, 1, 0, 500, default))
            .ReturnsAsync((
            [
                new("r1", UserId.ToString(), FeatureTypes.RoadLayers.Street, "Road 1", ToJsonElement("{}"), FixedUtcNow, null),
            ], 1));
        var ctrl = CreateController(fieldService: mockFieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetFeatures(type: FeatureTypes.Road);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<LoadFeaturesResponse<FieldFeatureResult>>(ok.Value);
        Assert.Single(response.Features);
        Assert.Equal(1, response.Count);
    }

    // ─── POST /api/field/inspect ─────────────────────────────────────────

    [Fact]
    public async Task SubmitInspection_InvalidFeatureId_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest("not-a-guid", FeatureTypes.Road, Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_InvalidType_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(Guid.NewGuid().ToString(), FeatureTypes.Area, Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_FeatureNotFound_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(Guid.NewGuid().ToString(), FeatureTypes.Road, Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_WrongCommune_ReturnsForbid()
    {
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureRegistryTypeAsync(roadId, default))
            .ReturnsAsync(FeatureTypes.Road);
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)2));
        var ctrl = CreateController(fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), FeatureTypes.Road, Json("{}"), "good"));

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task SubmitInspection_ValidRequest_Returns201()
    {
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureRegistryTypeAsync(roadId, default))
            .ReturnsAsync(FeatureTypes.Road);
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)1));
        fieldService.Setup(s => s.SubmitInspectionAsync(roadId, It.IsAny<Guid>(), FeatureTypes.Road, "good", It.IsAny<string>(), default))
            .ReturnsAsync(SubmitInspectionResult.Success(Guid.NewGuid()));
        var ctrl = CreateController(fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), FeatureTypes.Road, Json("""{"key": "val"}"""), "good"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var response = Assert.IsType<CreateResponse>(created.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task SubmitInspection_InvalidStatus_Returns400()
    {
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureRegistryTypeAsync(roadId, default))
            .ReturnsAsync(FeatureTypes.Road);
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)1));
        fieldService.Setup(s => s.SubmitInspectionAsync(roadId, It.IsAny<Guid>(), FeatureTypes.Road, "invalid_status", It.IsAny<string>(), default))
            .ReturnsAsync(SubmitInspectionResult.Failure(InspectionMalformedField.Status));
        var ctrl = CreateController(fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), FeatureTypes.Road, Json("{}"), "invalid_status"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task SubmitInspection_TypeMismatch_Returns400()
    {
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureRegistryTypeAsync(roadId, default))
            .ReturnsAsync(FeatureTypes.Road);
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)1));
        var ctrl = CreateController(fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), FeatureTypes.HouseEntrance, Json("{}"), "good"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
        fieldService.Verify(s => s.SubmitInspectionAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SubmitInspection_TypeMatch_Returns201()
    {
        var roadId = Guid.NewGuid();
        var fieldService = new Mock<IFieldService>();
        fieldService.Setup(s => s.GetFeatureRegistryTypeAsync(roadId, default))
            .ReturnsAsync(FeatureTypes.Road);
        fieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, roadId, default))
            .ReturnsAsync((OtherUserId, (int?)1));
        fieldService.Setup(s => s.SubmitInspectionAsync(roadId, It.IsAny<Guid>(), FeatureTypes.Road, "good", It.IsAny<string>(), default))
            .ReturnsAsync(SubmitInspectionResult.Success(Guid.NewGuid()));
        var ctrl = CreateController(fieldService: fieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.SubmitInspection(new FieldInspectRequest(roadId.ToString(), FeatureTypes.Road.ToUpperInvariant(), Json("{}"), "good"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    // ─── GET /api/field/inspections/{featureId} ──────────────────────────

    [Fact]
    public async Task GetInspections_ValidRequest_ReturnsInspections()
    {
        var featureId = Guid.NewGuid();
        var mockFieldService = new Mock<IFieldService>();
        mockFieldService.Setup(s => s.GetFeatureRegistryTypeAsync(featureId, default))
            .ReturnsAsync(FeatureTypes.Road);
        mockFieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, featureId, default))
            .ReturnsAsync((UserId, (int?)1));
        mockFieldService.Setup(s => s.GetInspectionsAsync(featureId, 0, 100, default))
            .ReturnsAsync(
            [
                new("i1", featureId.ToString(), FeatureTypes.Road, null, "good", FixedUtcNow),
                new("i2", featureId.ToString(), FeatureTypes.Road, null, "issue", FixedUtcNow.AddHours(1)),
            ]);
        var ctrl = CreateController(fieldService: mockFieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetInspections(featureId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FieldInspectionsResponse>(ok.Value);
        Assert.Equal(2, response.Inspections.Count);
        Assert.Equal("good", response.Inspections[0].Status);
        Assert.Equal("issue", response.Inspections[1].Status);
        Assert.Equal(FeatureTypes.Road, response.Inspections[0].Type);
    }

    [Fact]
    public async Task GetInspections_NoInspections_ReturnsEmptyList()
    {
        var featureId = Guid.NewGuid();
        var mockFieldService = new Mock<IFieldService>();
        mockFieldService.Setup(s => s.GetFeatureRegistryTypeAsync(featureId, default))
            .ReturnsAsync(FeatureTypes.Road);
        mockFieldService.Setup(s => s.GetFeatureOwnerAsync(FeatureTypes.Road, featureId, default))
            .ReturnsAsync((UserId, (int?)1));
        mockFieldService.Setup(s => s.GetInspectionsAsync(featureId, 0, 100, default))
            .ReturnsAsync([]);
        var ctrl = CreateController(fieldService: mockFieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.GetInspections(featureId);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<FieldInspectionsResponse>(ok.Value);
        Assert.Empty(response.Inspections);
    }

    // ─── POST /api/field/entrance ────────────────────────────────────────

    [Fact]
    public async Task CreateEntranceFromInspection_InvalidRoadId_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest("not-a-guid", Json("{}"), "Label"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_RoadNotFound_Returns400()
    {
        var ctrl = CreateController();
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(Guid.NewGuid().ToString(), Json("{}"), "Label"));

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_ValidRequest_Returns201()
    {
        var userId = UserId;
        var roadId = Guid.NewGuid();
        var mockFieldService = new Mock<IFieldService>();
        mockFieldService.Setup(s => s.GetRoadOwnerAsync(roadId, default))
            .ReturnsAsync((userId, (int?)1));
        mockFieldService.Setup(s => s.CreateEntranceAsync(roadId, userId, It.IsAny<Guid>(), "Entrance Label", It.IsAny<string>(), default))
            .ReturnsAsync(Guid.NewGuid());
        var ctrl = CreateController(fieldService: mockFieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(roadId.ToString(), Json("""{"coordinates": [[36.0, 3.0]]}"""), "Entrance Label"));

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
        var response = Assert.IsType<CreateResponse>(created.Value);
        Assert.True(response.Success);
    }

    [Fact]
    public async Task CreateEntranceFromInspection_DifferentCommune_ReturnsForbid()
    {
        var userId = UserId;
        var roadId = Guid.NewGuid();
        var mockFieldService = new Mock<IFieldService>();
        mockFieldService.Setup(s => s.GetRoadOwnerAsync(roadId, default))
            .ReturnsAsync((userId, (int?)2));
        var ctrl = CreateController(fieldService: mockFieldService.Object);
        SetUser(ctrl, UserRoles.FieldWorker, communeId: 1);

        var result = await ctrl.CreateEntranceFromInspection(new FieldEntranceCreateRequest(roadId.ToString(), Json("{}"), "Label"));

        Assert.IsType<ForbidResult>(result);
    }
}
