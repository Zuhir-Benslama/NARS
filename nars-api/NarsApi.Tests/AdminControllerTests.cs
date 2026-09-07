using System.Reflection;
using Microsoft.AspNetCore.Authorization;
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

public class AdminControllerTests
{
    private static AdminController CreateController(
        IAdminOverviewService? overview = null) =>
        new(overview ?? Mock.Of<IAdminOverviewService>(),
            Mock.Of<ILogger<AdminController>>(),
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
            .ReturnsAsync(([], 0));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<NationalOverviewResponse>(ok.Value);
        Assert.Equal("national", payload.Level);
        Assert.Empty(payload.Wilayas);
        Assert.Equal(0, payload.Total);
        overview.Verify(s => s.GetNationalOverviewAsync(0, 500, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Overview_NationalAdmin_NegativeSkip_IsClampedToZero()
    {
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetNationalOverviewAsync(0, 100, default))
            .ReturnsAsync(([], 0));
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.Overview(-5, 100, default);

        Assert.IsType<OkObjectResult>(result);
        overview.Verify(s => s.GetNationalOverviewAsync(0, 100, It.IsAny<CancellationToken>()), Times.Once);
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
        overview.Verify(s => s.GetWilayaReportAsync(1, It.IsAny<CancellationToken>()), Times.Once);
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
        overview.Setup(s => s.GetWilayaReportAsync(TestData.NonExistentId, default))
            .ReturnsAsync((WilayaReport?)null);
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetWilaya(TestData.NonExistentId, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public void GetWilaya_OnlyNationalAdmin_IsAuthorizedByRole()
    {
        var attr = typeof(AdminController)
            .GetMethod(nameof(AdminController.GetWilaya))
            ?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(UserRoles.NationalAdmin, attr.Roles);
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
        overview.Setup(s => s.GetDairaReportAsync(TestData.NonExistentId, default))
            .ReturnsAsync((DairaReport?)null);
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetDaira(TestData.NonExistentId, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public void GetDaira_WilayaOrNationalAdmin_IsAuthorizedByRole()
    {
        var attr = typeof(AdminController)
            .GetMethod(nameof(AdminController.GetDaira))
            ?.GetCustomAttribute<AuthorizeAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(UserRoles.WilayaOrNationalAdmin, attr.Roles);
    }

    [Fact]
    public async Task GetDaira_WilayaAdmin_WrongWilaya_ReturnsNotFound()
    {
        var overview = new Mock<IAdminOverviewService>();
        // The daira belongs to wilaya 1, but the caller is a wilaya_admin of wilaya 2.
        // GetDairaReportAsync(1, expectedWilayaId: 2) returns null, which the controller
        // surfaces as 404 — exercising the scope enforcement inside the report query.
        overview.Setup(s => s.GetDairaReportAsync(1, 2, default))
            .ReturnsAsync((DairaReport?)null);
        var ctrl = CreateController(overview.Object);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin, wilayaId: 2);

        var result = await ctrl.GetDaira(1, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
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
        var svc = new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), Mock.Of<IAccountLockoutService>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<IDateTimeProvider>(), Mock.Of<ISecurityStampCache>());
        Assert.Equal(expected, svc.CanCreateRole(caller, target));
    }
}
