using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Data;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class AdminControllerTests
{
    private static AppDbContext CreateDb() => CreateInMemoryDb("AdminTest");

    private static AdminController CreateController(AppDbContext db,
        IAdminOverviewService? overview = null) =>
        new(db,
            overview ?? Mock.Of<IAdminOverviewService>(),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static void SetUser(AdminController ctrl, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null)
    {
        ctrl.ControllerContext.HttpContext.User =
            AuthTestHelper.CreateClaimsPrincipal(Guid.NewGuid(), role, communeId, dairaId, wilayaId);
    }

    // ─── Overview ───────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_NationalAdmin_ReturnsNationalOverview()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetNationalOverviewAsync(default))
            .ReturnsAsync([]);
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Overview_WilayaAdmin_MissingWilayaId_Returns403()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.WilayaAdmin);

        var result = await ctrl.Overview(default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, problem.StatusCode);
    }

    [Fact]
    public async Task Overview_DairaAdmin_MissingDairaId_Returns403()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.DairaAdmin);

        var result = await ctrl.Overview(default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, problem.StatusCode);
    }

    [Fact]
    public async Task Overview_CommuneUser_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.Overview(default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Overview_WilayaAdmin_ValidId_ReturnsOk()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetWilayaReportAsync(1, default))
            .ReturnsAsync(new WilayaReport(1, "Alger", "", null, []));
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.WilayaAdmin, wilayaId: 1);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    [Fact]
    public async Task Overview_DairaAdmin_ValidId_ReturnsOk()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetDairaReportAsync(1, default))
            .ReturnsAsync(new DairaReport(1, "Draria", "", null, []));
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.Overview(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(ok.Value);
    }

    // ─── GetWilaya ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetWilaya_NationalAdmin_ValidId_ReturnsOk()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetWilayaReportAsync(1, default))
            .ReturnsAsync(new WilayaReport(1, "Alger", "", null, []));
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.GetWilaya(1, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<WilayaReport>(ok.Value);
        Assert.Equal(1, report.WilayaId);
    }

    [Fact]
    public async Task GetWilaya_NationalAdmin_UnknownId_ReturnsNotFound()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetWilayaReportAsync(999, default))
            .ReturnsAsync((WilayaReport?)null);
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.GetWilaya(999, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetWilaya_NonNationalAdmin_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.WilayaAdmin, wilayaId: 1);

        var result = await ctrl.GetWilaya(1, default);

        Assert.IsType<ForbidResult>(result);
    }

    // ─── GetDaira ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetDaira_NationalAdmin_ReturnsOk()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetDairaReportAsync(1, default))
            .ReturnsAsync(new DairaReport(1, "Draria", "", null, []));
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.GetDaira(1, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<DairaReport>(ok.Value);
        Assert.Equal(1, report.DairaId);
    }

    [Fact]
    public async Task GetDaira_NationalAdmin_UnknownId_ReturnsNotFound()
    {
        var db = CreateDb();
        var overview = new Mock<IAdminOverviewService>();
        overview.Setup(s => s.GetDairaReportAsync(999, default))
            .ReturnsAsync((DairaReport?)null);
        var ctrl = CreateController(db, overview.Object);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.GetDaira(999, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetDaira_CommuneUser_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.GetDaira(1, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetDaira_WilayaAdmin_WrongWilaya_ReturnsForbid()
    {
        var db = CreateDb();
        db.Dairas.Add(new Daira { DairaId = 1, WilayaId = 1, DairaFr = "Draria" });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.WilayaAdmin, wilayaId: 2);

        var result = await ctrl.GetDaira(1, default);

        Assert.IsType<ForbidResult>(result);
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
        var svc = new UserAuthorizationService(null!);
        Assert.Equal(expected, svc.CanCreateRole(caller, target));
    }
}
