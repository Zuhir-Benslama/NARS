using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class AdminControllerTests
{
    private static AdminController CreateController(
        IAdminOverviewService? overview = null) =>
        new(overview ?? Mock.Of<IAdminOverviewService>(),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    // ─── Overview ───────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_NationalAdmin_ReturnsNationalOverview()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetNationalOverviewAsync(0, 500, default))
            .ReturnsAsync((new List<WilayaSummary>(), 0));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Overview_WilayaAdmin_MissingWilayaId_Returns403()
    {
        var ctrl = CreateController();
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin);

        var result = await ctrl.Overview(default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, problem.StatusCode);
    }

    [Fact]
    public async Task Overview_DairaAdmin_MissingDairaId_Returns403()
    {
        var ctrl = CreateController();
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.DairaAdmin);

        var result = await ctrl.Overview(default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, problem.StatusCode);
    }

    [Fact]
    public async Task Overview_CommuneUser_ReturnsForbid()
    {
        var ctrl = CreateController();
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.Overview(default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Overview_WilayaAdmin_ValidId_ReturnsOk()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetWilayaReportAsync(1, default))
            .ReturnsAsync(new WilayaReport(1, "Alger", "", null, []));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin, wilayaId: 1);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var reportValue = Assert.IsType<WilayaReport>(ok.Value);
        Assert.Equal(1, reportValue.WilayaId);
    }

    [Fact]
    public async Task Overview_DairaAdmin_ValidId_ReturnsOk()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetDairaReportAsync(1, default))
            .ReturnsAsync(new DairaReport(1, "Draria", "", null, []));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var reportValue = Assert.IsType<DairaReport>(ok.Value);
        Assert.Equal(1, reportValue.DairaId);
    }

    // ─── GetWilaya ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetWilaya_NationalAdmin_ValidId_ReturnsOk()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetWilayaReportAsync(1, default))
            .ReturnsAsync(new WilayaReport(1, "Alger", "", null, []));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetWilaya(1, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<WilayaReport>(ok.Value);
        Assert.Equal(1, report.WilayaId);
    }

    [Fact]
    public async Task GetWilaya_NationalAdmin_UnknownId_ReturnsNotFound()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetWilayaReportAsync(999, default))
            .ReturnsAsync((WilayaReport?)null);
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetWilaya(999, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetWilaya_NonNationalAdmin_ReturnsForbid()
    {
        var ctrl = CreateController();
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin, wilayaId: 1);

        var result = await ctrl.GetWilaya(1, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ─── GetDaira ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetDaira_NationalAdmin_ReturnsOk()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetDairaReportAsync(1, default))
            .ReturnsAsync(new DairaReport(1, "Draria", "", null, []));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetDaira(1, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<DairaReport>(ok.Value);
        Assert.Equal(1, report.DairaId);
    }

    [Fact]
    public async Task GetDaira_NationalAdmin_UnknownId_ReturnsNotFound()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetDairaReportAsync(999, default))
            .ReturnsAsync((DairaReport?)null);
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetDaira(999, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetDaira_CommuneUser_ReturnsForbid()
    {
        var ctrl = CreateController();
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.GetDaira(1, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetDaira_WilayaAdmin_WrongWilaya_ReturnsNotFound()
    {
        var overview = new Mock<IAdminOverviewService>();
        // Return a Daira that belongs to a DIFFERENT wilaya (1) than the user's (2).
        // This exercises the authorization branch at AdminController.cs:67.
        overview.Setup(s => s.GetDairaByIdAsync(1, default))
            .ReturnsAsync(new NarsApi.Models.Daira { DairaId = 1, WilayaId = 1, DairaAr = "test", DairaFr = "test" });
        // Also mock the report — if the auth check were removed, the controller
        // would proceed here and return Ok, proving the 404 comes from authorization.
        overview.Setup(s => s.GetDairaReportAsync(1, default))
            .ReturnsAsync(new DairaReport(1, "test", "", null, []));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin, wilayaId: 2);

        var result = await ctrl.GetDaira(1, default);

        Assert.IsType<NotFoundResult>(result);
    }

    // ─── CanCreateRole (static helper) ──────────────────────────────────

    [Theory]
    [InlineData(UserRoles.NationalAdmin, UserRoles.WilayaAdmin, true)]
    [InlineData(UserRoles.WilayaAdmin, UserRoles.DairaAdmin, true)]
    [InlineData(UserRoles.DairaAdmin, UserRoles.CommuneUser, true)]
    [InlineData(UserRoles.CommuneUser, UserRoles.FieldWorker, true)]
    [InlineData(UserRoles.CommuneUser, UserRoles.NationalAdmin, false)]
    [InlineData(UserRoles.NationalAdmin, UserRoles.NationalAdmin, false)]
    [InlineData(UserRoles.FieldWorker, UserRoles.CommuneUser, false)]
    [InlineData(UserRoles.DairaAdmin, UserRoles.WilayaAdmin, false)]
    public void CanCreateRole_ValidatesCorrectly(string caller, string target, bool expected)
    {
        // CanCreateRole is a pure role-hierarchy check (no DB query).
        // InMemory DB is only needed to satisfy the UserAuthorizationService constructor.
        using var db = CreateInMemoryDb("AdminControllerRoleTest");
        var svc = new UserAuthorizationService(db);
        Assert.Equal(expected, svc.CanCreateRole(caller, target));
    }
}
