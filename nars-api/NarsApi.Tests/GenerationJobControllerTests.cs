using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
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
/// Unit tests for <see cref="GenerationJobController"/> (mocked service):
/// request validation, exception mapping and how per-chunk uploads authenticate
/// before reaching the queue service.
/// </summary>
public class GenerationJobControllerTests
{
    private static readonly GenerationJobView EmptyView = new(
        Guid.CreateVersion7(), CommuneId100, GenerationJob.StatusPending, GenerationJob.StageSegment,
        1, 0, 0.0, [], null, DateTimeOffset.UtcNow, null, [], null);

    private static (GenerationJobController ctrl, Mock<IGenerationJobService> svc) CreateController(
        string role, int? communeId = null, int? dairaId = null, int? wilayaId = null)
    {
        var svc = new Mock<IGenerationJobService>();
        var ctrl = new GenerationJobController(
            svc.Object, Mock.Of<ILogger<GenerationJobController>>(), Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, UserId, role, communeId, dairaId, wilayaId);
        return (ctrl, svc);
    }

    private static Mock<IFormFile> CreateRaster(
        string contentType = "image/png", string fileName = "chunk.png")
    {
        var file = new Mock<IFormFile>();
        file.SetupGet(f => f.Length).Returns(1);
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

    // ── POST /api/generation/jobs ────────────────────────────────────────────

    [Fact]
    public async Task Create_MissingCommuneId_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);

        var result = await ctrl.Create(new CreateGenerationJobRequest { Grids = [Grid("0,0")] }, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Create_WithoutGrids_ReturnsBadRequest()
    {
        var (ctrl, _) = CreateController(UserRoles.NationalAdmin);

        var result = await ctrl.Create(new CreateGenerationJobRequest { CommuneId = CommuneId100 }, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Create_DelegatesToServiceAndReturnsCreated()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.CreateAsync(
                UserRoles.NationalAdmin, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), CommuneId100, It.IsAny<IReadOnlyList<GenerationGridDto>>(), default))
            .ReturnsAsync(EmptyView);

        var result = await ctrl.Create(
            new CreateGenerationJobRequest { CommuneId = CommuneId100, Grids = [Grid("0,0")] }, default);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(201, created.StatusCode);
        Assert.Equal(nameof(GenerationJobController.Get), created.ActionName);
        svc.Verify(s => s.CreateAsync(
            UserRoles.NationalAdmin, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<Guid>(), CommuneId100, It.IsAny<IReadOnlyList<GenerationGridDto>>(), default), Times.Once);
    }

    [Fact]
    public async Task Create_ForbiddenScope_MapsToForbid()
    {
        var (ctrl, svc) = CreateController(UserRoles.FieldWorker);
        svc.Setup(s => s.CreateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<Guid>(), CommuneId100, It.IsAny<IReadOnlyList<GenerationGridDto>>(), default))
            .ThrowsAsync(new UnauthorizedAccessException());

        var result = await ctrl.Create(
            new CreateGenerationJobRequest { CommuneId = CommuneId100, Grids = [Grid("0,0")] }, default);

        Assert.IsType<ForbidResult>(result.Result);
    }

    // ── GET /api/generation/jobs/{id} ────────────────────────────────────────

    [Fact]
    public async Task Get_UnknownJob_MapsToNotFound()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);
        svc.Setup(s => s.GetViewAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), default))
            .ThrowsAsync(new KeyNotFoundException());

        var result = await ctrl.Get(Guid.CreateVersion7(), default);

        ExpectProblem(result.Result!, StatusCodes.Status404NotFound);
    }

    // ── POST /api/generation/jobs/{id}/chunks/{chunkId}/raster ───────────────

    [Fact]
    public async Task UploadChunkRaster_RejectsNonImageContentType()
    {
        var (ctrl, _) = CreateController(UserRoles.FieldWorker);
        var upload = new JobRasterUploadRequest { Raster = CreateRaster(contentType: "text/plain").Object };

        var result = await ctrl.UploadChunkRaster(Guid.CreateVersion7(), Guid.CreateVersion7(), upload, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadChunkRaster_RejectsMissingExtension()
    {
        var (ctrl, _) = CreateController(UserRoles.FieldWorker);
        var upload = new JobRasterUploadRequest { Raster = CreateRaster(fileName: "chunk").Object };

        var result = await ctrl.UploadChunkRaster(Guid.CreateVersion7(), Guid.CreateVersion7(), upload, default);

        ExpectProblem(result.Result!, StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task UploadChunkRaster_DelegatesToService()
    {
        var (ctrl, svc) = CreateController(UserRoles.FieldWorker, communeId: CommuneId100);
        var jobId = Guid.CreateVersion7();
        var chunkId = Guid.CreateVersion7();
        svc.Setup(s => s.UploadChunkRasterAsync(
                jobId, chunkId, UserRoles.FieldWorker, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<byte[]>(), "chunk.png", "image/png", default))
            .ReturnsAsync(EmptyView);

        var result = await ctrl.UploadChunkRaster(jobId, chunkId,
            new JobRasterUploadRequest { Raster = CreateRaster().Object }, default);

        Assert.Equal(200, ((ObjectResult)result.Result!).StatusCode);
        svc.Verify(s => s.UploadChunkRasterAsync(
            jobId, chunkId, UserRoles.FieldWorker, It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<byte[]>(), "chunk.png", "image/png", default), Times.Once);
    }

    // ── POST /api/generation/jobs/{id}/cancel ────────────────────────────────

    [Fact]
    public async Task Cancel_DelegatesToService()
    {
        var (ctrl, svc) = CreateController(UserRoles.WilayaAdmin);
        var jobId = Guid.CreateVersion7();
        svc.Setup(s => s.CancelAsync(jobId, It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(), default))
            .ReturnsAsync(EmptyView);

        var result = await ctrl.Cancel(jobId, default);

        Assert.Equal(200, ((ObjectResult)result.Result!).StatusCode);
    }

    private static GenerationGridDto Grid(string key)
        => new(key, 18, 4200, 3400, 512, 512, 2.94, 36.70, 2.95, 36.71);
}
