using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Unit tests for <see cref="DraftFeaturesController"/> (mocked service).
/// Covers the request-validation, exception-mapping and review-result
/// translation glue that the service-level suites do not touch.
/// </summary>
public class DraftFeaturesControllerTests
{
    private static (DraftFeaturesController ctrl, Mock<IDraftFeaturesService> svc) CreateController(
        string role, int? communeId = null, int? dairaId = null, int? wilayaId = null)
    {
        var svc = new Mock<IDraftFeaturesService>();
        var ctrl = new DraftFeaturesController(
            svc.Object,
            Mock.Of<IRoadGenerationService>(),
            Mock.Of<ILogger<DraftFeaturesController>>(),
            Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, UserId, role, communeId, dairaId, wilayaId);
        return (ctrl, svc);
    }

    private static Mock<IFormFile> CreateTile(
        string contentType = "image/png", string fileName = "tile.png", long length = 1)
    {
        var file = new Mock<IFormFile>();
        file.SetupGet(f => f.Length).Returns(length);
        file.SetupGet(f => f.FileName).Returns(fileName);
        file.SetupGet(f => f.ContentType).Returns(contentType);
        file.Setup(f => f.OpenReadStream()).Returns(new MemoryStream([1, 2, 3]));
        return file;
    }

    private static ObjectResult ExpectProblem(IActionResult result, int statusCode)
    {
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, problem.StatusCode);
        return problem;
    }

    // ── POST /api/draft-features/segment ─────────────────────────────────────

    [Fact]
    public async Task SegmentTile_MissingCommuneId_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest { Tile = CreateTile().Object };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_EmptyTile_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile(length: 0).Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_InvalidFeatureType_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            FeatureType = "banana",
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_DisallowedContentType_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile(contentType: "application/octet-stream").Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_UnsupportedExtension_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile(fileName: "tile.exe").Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_NoExtension_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile(fileName: "tile").Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_MissingBoundingBox_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            MinLon = null,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task SegmentTile_Success_ReturnsSummaryAndDefaultsToBuilding()
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, communeId: CommuneId100);
        var summary = new SegmentSummaryResponse { BuildingCount = 3, RoadCount = 0, DraftIds = [Guid.NewGuid()] };
        svc.Setup(s => s.SegmentTileAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            MinLon = 1.5,
            MinLat = 1.5,
            MaxLon = 2.5,
            MaxLat = 2.5,
        };

        var result = await ctrl.SegmentTile(request, default);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(summary, ok.Value);
        svc.Verify(s => s.SegmentTileAsync(
            UserRoles.CommuneUser, CommuneId100, null, null,
            CommuneId100, AiDraftFeature.TypeBuilding,
            It.IsAny<Stream>(), "tile.png", "image/png",
            It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SegmentTile_ExplicitRoadType_IsPassedThroughNormalized()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.SegmentTileAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SegmentSummaryResponse());
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            FeatureType = "  Road ",
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        await ctrl.SegmentTile(request, default);

        svc.Verify(s => s.SegmentTileAsync(
            UserRoles.NationalAdmin, null, null, null,
            CommuneId100, AiDraftFeature.TypeRoad,
            It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SegmentTile_OutOfScope_ReturnsForbid()
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, communeId: CommuneId100);
        svc.Setup(s => s.SegmentTileAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        Assert.IsType<ForbidResult>(result.Result!);
    }

    [Fact]
    public async Task SegmentTile_UnknownCommune_ReturnsNotFound()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.SegmentTileAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException());
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task SegmentTile_SegmentationUnavailable_ReturnsBadGateway()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.SegmentTileAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<(double, double, double, double)>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SegmentationServiceException("down"));
        var request = new SegmentTileRequest
        {
            CommuneId = CommuneId100,
            Tile = CreateTile().Object,
            MinLon = 1,
            MinLat = 1,
            MaxLon = 2,
            MaxLat = 2,
        };

        var result = await ctrl.SegmentTile(request, default);

        ExpectProblem(result.Result!, StatusCodes.Status502BadGateway);
    }

    // ── GET /api/draft-features ──────────────────────────────────────────────

    [Fact]
    public async Task ListDrafts_InvalidStatus_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);

        var result = await ctrl.ListDrafts(CommuneId100, null, status: "bogus");

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ListDrafts_InvalidFeatureType_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);

        var result = await ctrl.ListDrafts(CommuneId100, featureType: "parcel");

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task ListDrafts_Success_ClampsPaginationAndReturns()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        var page = new PagedResponse<AiDraftFeatureDto>(
            Items: [new AiDraftFeatureDto(Guid.NewGuid(), AiDraftFeature.TypeRoad,
                """{"type":"LineString"}""", 0.9, AiDraftFeature.StatusPending, FixedUtcNowOffset)],
            Total: 1, Skip: 0, Take: 100);
        svc.Setup(s => s.ListDraftsAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(page);

        var result = await ctrl.ListDrafts(CommuneId100, AiDraftFeature.TypeRoad, status: "pending", skip: -5, take: 1000);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(page, ok.Value);
        svc.Verify(s => s.ListDraftsAsync(
            UserRoles.NationalAdmin, null, null, null,
            CommuneId100, AiDraftFeature.TypeRoad, AiDraftFeature.StatusPending,
            0, Pagination.MaxTake, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ListDrafts_OutOfScope_ReturnsForbid()
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, communeId: CommuneId100);
        svc.Setup(s => s.ListDraftsAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());

        var result = await ctrl.ListDrafts(CommuneId101, null, status: "pending");

        Assert.IsType<ForbidResult>(result.Result!);
    }

    // ── Accept / Reject / Update / Delete review glue ────────────────────────

    [Fact]
    public async Task AcceptDraft_Success_ReturnsNoContent()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.AcceptDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.Success));
        var draftId = Guid.NewGuid();

        var result = await ctrl.AcceptDraft(draftId, default);

        Assert.IsType<NoContentResult>(result);
        svc.Verify(s => s.AcceptDraftAsync(
            UserRoles.NationalAdmin, null, null, null, UserId, draftId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RejectDraft_Success_ReturnsNoContent()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.RejectDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.Success));
        var draftId = Guid.NewGuid();

        var result = await ctrl.RejectDraft(draftId, default);

        Assert.IsType<NoContentResult>(result);
        svc.Verify(s => s.RejectDraftAsync(
            UserRoles.NationalAdmin, null, null, null, UserId, draftId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AcceptDraft_NotFound_ReturnsNotFound()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.AcceptDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.NotFound));

        var result = await ctrl.AcceptDraft(Guid.NewGuid(), default);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task AcceptDraft_RulesNotMet_ReturnsUnprocessableEntity()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.AcceptDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.RulesNotMet));

        var result = await ctrl.AcceptDraft(Guid.NewGuid(), default);

        ExpectProblem(result, StatusCodes.Status422UnprocessableEntity);
    }

    [Fact]
    public async Task AcceptDraft_AlreadyReviewed_ReturnsConflict()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.AcceptDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.AlreadyReviewed));

        var result = await ctrl.AcceptDraft(Guid.NewGuid(), default);

        ExpectProblem(result, StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task AcceptDraft_InvalidGeometry_ReturnsBadRequest()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.AcceptDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.InvalidGeometry));

        var result = await ctrl.AcceptDraft(Guid.NewGuid(), default);

        ExpectProblem(result, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task AcceptDraft_Forbidden_ReturnsForbid()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.AcceptDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.Forbidden));

        var result = await ctrl.AcceptDraft(Guid.NewGuid(), default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task UpdateDraft_Success_TrimsAndForwardsGeometry()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.UpdateDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.Success));
        var draftId = Guid.NewGuid();
        const string unpadded = """{"type":"LineString"}""";

        var result = await ctrl.UpdateDraft(draftId, new DraftUpdateRequest { GeometryGeoJson = $"  {unpadded}  " }, default);

        Assert.IsType<NoContentResult>(result);
        svc.Verify(s => s.UpdateDraftAsync(
            UserRoles.NationalAdmin, null, null, null, UserId, draftId, unpadded, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteDraft_Success_ReturnsNoContent()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.DeleteDraftAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DraftReviewResult(DraftReviewStatus.Success));
        var draftId = Guid.NewGuid();

        var result = await ctrl.DeleteDraft(draftId, default);

        Assert.IsType<NoContentResult>(result);
        svc.Verify(s => s.DeleteDraftAsync(
            UserRoles.NationalAdmin, null, null, null, UserId, draftId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── POST /api/draft-features/generate-roads ──────────────────────────────

    private static (DraftFeaturesController ctrl, Mock<IRoadGenerationService> roadGen) CreateGenerateController(
        string role, int? communeId = null)
    {
        var roadGen = new Mock<IRoadGenerationService>();
        var ctrl = new DraftFeaturesController(
            Mock.Of<IDraftFeaturesService>(),
            roadGen.Object,
            Mock.Of<ILogger<DraftFeaturesController>>(),
            Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, UserId, role, communeId, null, null);
        return (ctrl, roadGen);
    }

    private static OkObjectResult ExpectOk(IActionResult result)
        => Assert.IsType<OkObjectResult>(result);

    [Fact]
    public async Task GenerateRoads_MissingCommuneId_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateGenerateController(UserRoles.NationalAdmin);
        var body = new GenerateRoadsRequest { CommuneId = null, DraftIds = [Guid.NewGuid()] };

        ActionResult<GenerateRoadsResponse> result = await ctrl.GenerateRoads(body, default);

        ExpectProblem(result.Result!, 400);
    }

    [Fact]
    public async Task GenerateRoads_EmptyDraftIds_ReturnsBadRequest()
    {
        var (ctrl, roadGen) = CreateGenerateController(UserRoles.CommuneUser, communeId: CommuneId100);
        ctrl.ModelState.AddModelError("DraftIds", "DraftIds must contain at least one id.");
        var body = new GenerateRoadsRequest { CommuneId = CommuneId100, DraftIds = [] };

        ActionResult<GenerateRoadsResponse> result = await ctrl.GenerateRoads(body, default);

        Assert.IsType<BadRequestObjectResult>(result.Result!);
        roadGen.Verify(s => s.GenerateAsync(
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GenerateRoads_Success_ReturnsCreatedRoads()
    {
        var (ctrl, roadGen) = CreateGenerateController(UserRoles.CommuneUser, communeId: CommuneId100);
        var draftId = Guid.NewGuid();
        var roadId = Guid.NewGuid();
        var data = JsonNode.Parse("""{"type":"road","coordinates":[{"lat":36.716,"lng":2.951}]}""")!.AsObject();
        roadGen.Setup(s => s.GenerateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                UserId, CommuneId100, It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoadGenerationSummary(
                [new GeneratedRoad(roadId, FeatureTypes.RoadLayers.Street, "", data)],
                Dropped: 2,
                new RoadDropBreakdown(TooShort: 0, LowConfidence: 0, ExcessiveTurnAngle: 0, OutsideUrbanArea: 2, TooClose: 0, InvalidGeometry: 0)));
        var body = new GenerateRoadsRequest { CommuneId = CommuneId100, DraftIds = [draftId] };

        ActionResult<GenerateRoadsResponse> result = await ctrl.GenerateRoads(body, default);

        var ok = ExpectOk(result.Result!);
        var response = Assert.IsType<GenerateRoadsResponse>(ok.Value);
        Assert.Equal(2, response.Dropped);
        var road = Assert.Single(response.Created);
        Assert.Equal(roadId, road.DbId);
        Assert.Equal(FeatureTypes.RoadLayers.Street, road.Layer);
        Assert.Equal("road", road.Data.GetProperty("type").GetString());
        roadGen.Verify(s => s.GenerateAsync(
            UserRoles.CommuneUser, CommuneId100, null, null, UserId, CommuneId100,
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { draftId })), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GenerateRoads_ForbiddenCommune_ReturnsForbid()
    {
        var (ctrl, roadGen) = CreateGenerateController(UserRoles.CommuneUser, communeId: CommuneId100);
        roadGen.Setup(s => s.GenerateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<IReadOnlyList<Guid>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException());
        var body = new GenerateRoadsRequest { CommuneId = CommuneId101, DraftIds = [Guid.NewGuid()] };

        ActionResult<GenerateRoadsResponse> result = await ctrl.GenerateRoads(body, default);

        Assert.IsType<ForbidResult>(result.Result!);
    }
}
