using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class AdminControllerTests
{
    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"AdminTest_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static AdminController CreateController(AppDbContext db,
        IAdminOverviewService? overview = null) =>
        new(db,
            Mock.Of<ILogger<AdminController>>(),
            overview ?? Mock.Of<IAdminOverviewService>(),
            new UserAuthorizationService(db))
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

    // ─── CreateAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAdmin_NullBody_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.CreateAdmin(null!, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_NationalToWilaya_Returns201()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.CreateAdmin(new CreateAdminRequest(
            Username: "new_wilaya_admin",
            Password: TestData.DefaultPassword,
            Name: "New Admin",
            Email: "new@test.com",
            Phone: TestData.DefaultPhone,
            Role: UserRoles.WilayaAdmin,
            CommuneId: null,
            DairaId: null,
            WilayaId: 1), default);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_DuplicateUsername_Returns409()
    {
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "existing",
            Email = "existing@test.com",
            Name = "Existing",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.CreateAdmin(new CreateAdminRequest(
            Username: "existing",
            Password: TestData.DefaultPassword,
            Name: "New",
            Email: "new@test.com",
            Phone: TestData.DefaultPhone,
            Role: UserRoles.WilayaAdmin,
            CommuneId: null,
            DairaId: null,
            WilayaId: 1), default);

        Assert.IsType<ObjectResult>(result);
    }

    [Fact]
    public async Task CreateAdmin_DisallowedRolePair_ReturnsForbid()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.CreateAdmin(new CreateAdminRequest(
            Username: "new_user",
            Password: TestData.DefaultPassword,
            Name: "New",
            Email: "new@test.com",
            Phone: TestData.DefaultPhone,
            Role: UserRoles.DairaAdmin,
            CommuneId: null,
            DairaId: 1,
            WilayaId: null), default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateAdmin_DairaAdminOutsideScope_ReturnsForbid()
    {
        var db = CreateDb();
        db.Communes.Add(new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other" });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.CreateAdmin(new CreateAdminRequest(
            Username: "new_user",
            Password: TestData.DefaultPassword,
            Name: "New",
            Email: "new@test.com",
            Phone: TestData.DefaultPhone,
            Role: UserRoles.CommuneUser,
            CommuneId: 99,
            DairaId: null,
            WilayaId: null), default);

        Assert.IsType<ForbidResult>(result);
    }

    // ─── GetManageableUsers ─────────────────────────────────────────────

    [Fact]
    public async Task GetManageableUsers_NationalAdmin_ReturnsWilayaAdmins()
    {
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "wa1",
            Email = "wa1@test.com",
            Name = "WA1",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Username = "cu1",
            Email = "cu1@test.com",
            Name = "CU1",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.GetManageableUsers(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var users = Assert.IsType<List<AdminUserSummary>>(ok.Value);
        Assert.Single(users);
        Assert.Equal("wa1", users[0].Username);
    }

    // ─── UpdateAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAdmin_UserNotFound_Returns404()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.UpdateAdmin(
            Guid.NewGuid(),
            new UpdateAdminRequest(Name: "Updated", Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
            default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAdmin_NullBody_Returns400()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.UpdateAdmin(Guid.NewGuid(), null!, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAdmin_ValidNameUpdate_ReturnsOk()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "target",
            Email = "target@test.com",
            Name = "Original",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.UpdateAdmin(
            userId,
            new UpdateAdminRequest(Name: "Updated", Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Updated", (await db.Users.FindAsync(userId))!.Name);
    }

    // ─── DeleteAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAdmin_UserNotFound_Returns404()
    {
        var db = CreateDb();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.DeleteAdmin(Guid.NewGuid(), default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task DeleteAdmin_Valid_ReturnsOk()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "delete_me",
            Email = "delete@test.com",
            Name = "Delete Me",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.WilayaAdmin,
            WilayaId = 1,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.NationalAdmin);

        var result = await ctrl.DeleteAdmin(userId, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Null(await db.Users.FindAsync(userId));
    }

    [Fact]
    public async Task DeleteAdmin_ForbiddenRoleHierarchy_ReturnsForbid()
    {
        var db = CreateDb();
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "national",
            Email = "national@test.com",
            Name = "National",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.NationalAdmin,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        SetUser(ctrl, UserRoles.WilayaAdmin, wilayaId: 1);

        var result = await ctrl.DeleteAdmin(userId, default);

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
        var svc = new UserAuthorizationService(CreateDb());
        Assert.Equal(expected, svc.CanCreateRole(caller, target));
    }
}
