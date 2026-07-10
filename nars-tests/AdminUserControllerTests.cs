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

public class AdminUserControllerTests
{
    private static AppDbContext CreateDb() => CreateInMemoryDb("AdminUserTest");

    private static AdminUserController CreateController(AppDbContext db) =>
        new(db,
            Mock.Of<ILogger<AdminUserController>>(),
            new UserAuthorizationService(db),
            AuthTestHelper.CreateUserCreationMock(db),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    private static void SetUser(AdminUserController ctrl, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null)
    {
        ctrl.ControllerContext.HttpContext.User =
            AuthTestHelper.CreateClaimsPrincipal(Guid.NewGuid(), role, communeId, dairaId, wilayaId);
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

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, problem.StatusCode);
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
}
