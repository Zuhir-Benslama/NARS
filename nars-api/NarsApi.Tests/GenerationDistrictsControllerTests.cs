using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

/// <summary>
/// Unit tests for <see cref="GenerationDistrictsController"/> (mocked service):
/// request validation and exception mapping for the standalone districts phase.
/// </summary>
public class GenerationDistrictsControllerTests
{
    private static (GenerationDistrictsController ctrl, Mock<IDistrictGenerationService> svc)
        CreateController(string role, int? communeId = null, int? dairaId = null, int? wilayaId = null)
    {
        var svc = new Mock<IDistrictGenerationService>();
        var ctrl = new GenerationDistrictsController(
            svc.Object,
            Mock.Of<ILogger<GenerationDistrictsController>>(),
            Mock.Of<IWebHostEnvironment>());
        AuthTestHelper.SetUser(ctrl, UserId, role, communeId, dairaId, wilayaId);
        return (ctrl, svc);
    }

    private static DistrictGenerationSummary Summary(int count = 2) => new(
        Enumerable.Range(0, count)
            .Select(i => new GeneratedDistrictDraft(
                Guid.CreateVersion7(), "{}", 60_000 + i, 36.7 + i, 2.95 + i))
            .ToList(),
        AbsorbedSlivers: 1,
        PrimaryRoadCount: 4,
        UrbanAreaCount: 2);

    private static ObjectResult ExpectProblem(IActionResult result, int statusCode)
    {
        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(statusCode, problem.StatusCode);
        return problem;
    }

    private static DistrictGenerationSummary EmptySummary => new(
        [], AbsorbedSlivers: 0, PrimaryRoadCount: 0, UrbanAreaCount: 0);

    // ── POST /api/generation/districts ───────────────────────────────────────

    [Fact]
    public async Task Generate_MissingCommuneId_ReturnsBadRequest()
    {
        var (ctrl, svc) = CreateController(UserRoles.NationalAdmin);

        var result = await ctrl.Generate(new GenerateDistrictsRequest(), CancellationToken.None);

        ExpectProblem(result.Result!, 400);
        svc.Verify(s => s.GenerateAsync(
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Generate_ReturnsOkWithDrafts()
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, CommuneId100);
        svc.Setup(s => s.GenerateAsync(
                UserRoles.CommuneUser, CommuneId100, null, null, CommuneId100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Summary(3));

        var result = await ctrl.Generate(
            new GenerateDistrictsRequest { CommuneId = CommuneId100 }, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var body = Assert.IsType<GenerateDistrictsResponse>(ok.Value);
        Assert.Equal(3, body.Districts.Count);
        Assert.Equal(1, body.AbsorbedSlivers);
        Assert.Equal(4, body.PrimaryRoadCount);
        Assert.Equal(2, body.UrbanAreaCount);
    }

    [Fact]
    public async Task Generate_PassesCallerScopeToService()
    {
        var (ctrl, svc) = CreateController(UserRoles.DairaAdmin, communeId: null, dairaId: 7, wilayaId: 9);
        svc.Setup(s => s.GenerateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmptySummary);

        await ctrl.Generate(new GenerateDistrictsRequest { CommuneId = CommuneId100 }, CancellationToken.None);

        svc.Verify(s => s.GenerateAsync(
            UserRoles.DairaAdmin, null, 7, 9, CommuneId100, It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Generate_NoAccessToCommune_ReturnsForbid()
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, CommuneId100);
        svc.Setup(s => s.GenerateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException("Caller has no access to the commune."));

        var result = await ctrl.Generate(
            new GenerateDistrictsRequest { CommuneId = CommuneId101 }, CancellationToken.None);

        Assert.IsType<ForbidResult>(result.Result);
    }

    [Theory]
    [InlineData("District generation needs at least one urban area (central_urban or secondary_urban) to partition.")]
    [InlineData("Commune 1 has no boulevard or avenue roads.")]
    public async Task Generate_MissingInput_ReturnsBadRequestWithServiceMessage(string message)
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, CommuneId100);
        svc.Setup(s => s.GenerateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(message));

        var result = await ctrl.Generate(
            new GenerateDistrictsRequest { CommuneId = CommuneId100 }, CancellationToken.None);

        // Surfaced verbatim: the user has to be able to act on it.
        var problem = ExpectProblem(result.Result!, 400);
        var details = Assert.IsType<ProblemDetails>(problem.Value);
        Assert.Equal(message, details.Detail);
    }

    [Fact]
    public async Task Generate_NonPostgisHost_ReturnsNotImplemented()
    {
        var (ctrl, svc) = CreateController(UserRoles.CommuneUser, CommuneId100);
        svc.Setup(s => s.GenerateAsync(
                It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<int?>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("District generation requires a PostGIS-backed database."));

        var result = await ctrl.Generate(
            new GenerateDistrictsRequest { CommuneId = CommuneId100 }, CancellationToken.None);

        ExpectProblem(result.Result!, 501);
    }
}
