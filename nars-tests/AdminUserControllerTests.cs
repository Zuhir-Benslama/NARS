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
        new(Mock.Of<ILogger<AdminUserController>>(),
            new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), Mock.Of<IDateTimeProvider>()),
            new UserCreationService(db, new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), Mock.Of<IDateTimeProvider>()), Mock.Of<ILogger<UserCreationService>>()),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

    // ─── CreateAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAdmin_NullBody_Returns400()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.CreateManagedUser(null!, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task CreateAdmin_NationalToWilaya_Returns201()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.CreateManagedUser(new CreateAdminRequest(
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
        using var db = CreateDb();
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
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.CreateManagedUser(new CreateAdminRequest(
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
        using var db = CreateDb();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.CreateManagedUser(new CreateAdminRequest(
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
        using var db = CreateDb();
        db.Communes.Add(new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other" });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.CreateManagedUser(new CreateAdminRequest(
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
        using var db = CreateDb();
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
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.GetManageableUsers(default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResponse<AdminUserSummary>>(ok.Value);
        Assert.Single(page.Items);
        Assert.Equal("wa1", page.Items[0].Username);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task GetManageableUsers_NegativeSkip_IsClampedToZero()
    {
        using var db = CreateDb();
        db.Users.AddRange(
            new User
            {
                Id = Guid.NewGuid(),
                Username = "wa1",
                Email = "wa1@test.com",
                Name = "WA1",
                Phone = TestData.DefaultPhone,
                PasswordHash = "hash",
                Role = UserRoles.WilayaAdmin,
                WilayaId = 1,
            },
            new User
            {
                Id = Guid.NewGuid(),
                Username = "wa2",
                Email = "wa2@test.com",
                Name = "WA2",
                Phone = TestData.DefaultPhone,
                PasswordHash = "hash",
                Role = UserRoles.WilayaAdmin,
                WilayaId = 1,
            });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        // With take=1, an un-clamped skip of -10 would return the empty slice; the
        // clamped skip of 0 must return the first user in username order.
        var result = await ctrl.GetManageableUsers(-10, 1, default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var page = Assert.IsType<PagedResponse<AdminUserSummary>>(ok.Value);
        var user = Assert.Single(page.Items);
        Assert.Equal("wa1", user.Username);
    }

    // ─── UpdateAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAdmin_UserNotFound_Returns404()
    {
        using var db = CreateDb();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.UpdateManagedUser(
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
        using var db = CreateDb();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.UpdateManagedUser(Guid.NewGuid(), null!, default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, problem.StatusCode);
    }

    [Fact]
    public async Task UpdateAdmin_ValidNameUpdate_ReturnsOk()
    {
        using var db = CreateDb();
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
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.UpdateManagedUser(
            userId,
            new UpdateAdminRequest(Name: "Updated", Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal("Updated", (await db.Users.FindAsync(userId))!.Name);
    }

    [Fact]
    public async Task UpdateAdmin_DairaAdminReassignsCommuneUserOutsideDaira_ReturnsForbid()
    {
        using var db = CreateDb();
        db.Communes.AddRange(
            new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" },
            new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other daira" });
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = TestData.DefaultPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestData.DefaultPassword),
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "commune_user",
            Email = "cu@test.com",
            Name = "Commune User",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 5,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, callerId, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.UpdateManagedUser(
            userId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: 99, DairaId: null, WilayaId: null,
                Password: TestData.DefaultPassword),
            default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(5, (await db.Users.FindAsync(userId))!.CommuneId);
    }

    [Fact]
    public async Task UpdateAdmin_DairaAdminReassignsCommuneUserWithinDaira_ReturnsOk()
    {
        using var db = CreateDb();
        db.Communes.AddRange(
            new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" },
            new Commune { CommuneId = 6, DairaId = 1, CommuneFr = "Commune B" });
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = TestData.DefaultPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestData.DefaultPassword),
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "commune_user",
            Email = "cu@test.com",
            Name = "Commune User",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 5,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, callerId, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.UpdateManagedUser(
            userId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: 6, DairaId: null, WilayaId: null,
                Password: TestData.DefaultPassword),
            default);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<ApiResponse>(ok.Value);
        Assert.True(response.Success);
        Assert.Equal(6, (await db.Users.FindAsync(userId))!.CommuneId);
    }

    [Fact]
    public async Task UpdateAdmin_CommuneUserMovesFieldWorkerOutsideCommune_ReturnsForbid()
    {
        using var db = CreateDb();
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = TestData.DefaultPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(TestData.DefaultPassword),
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "field_worker",
            Email = "fw@test.com",
            Name = "Field Worker",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.FieldWorker,
            CommuneId = 1,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, callerId, UserRoles.CommuneUser, communeId: 1);

        var result = await ctrl.UpdateManagedUser(
            userId,
            new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                Role: null, CommuneId: 2, DairaId: null, WilayaId: null,
                Password: TestData.DefaultPassword),
            default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal(1, (await db.Users.FindAsync(userId))!.CommuneId);
    }

    [Fact]
    public async Task UpdateAdmin_ProfileOnlyEditOutsideScope_ReturnsForbid()
    {
        using var db = CreateDb();
        db.Communes.AddRange(
            new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" },
            new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other daira" });
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "commune_user",
            Email = "cu@test.com",
            Name = "Commune User",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 99,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, callerId, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.UpdateManagedUser(
            userId,
            new UpdateAdminRequest(Name: "Sneaky", Email: null, Phone: null,
                Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
            default);

        Assert.IsType<ForbidResult>(result);
        Assert.Equal("Commune User", (await db.Users.FindAsync(userId))!.Name);
    }

    [Fact]
    public async Task UpdateAdmin_ProfileOnlyEditWithinScope_ReturnsOk()
    {
        using var db = CreateDb();
        db.Communes.Add(new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" });
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "commune_user",
            Email = "cu@test.com",
            Name = "Commune User",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 5,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, callerId, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.UpdateManagedUser(
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
        using var db = CreateDb();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

        var result = await ctrl.DeleteManagedUser(Guid.NewGuid(), default);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    // DeleteAdmin_Valid_ReturnsNoContent and DeleteAdmin_WithinScope_ReturnsNoContent
    // live in AdminControllerServiceTests (PostgreSQL-backed) because the InMemory
    // provider does not support ExecuteDeleteAsync/ExecuteUpdateAsync used by
    // UserAuthorizationService.DeleteUserAsync.

    [Fact]
    public async Task DeleteAdmin_ForbiddenRoleHierarchy_ReturnsForbid()
    {
        using var db = CreateDb();
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
        AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin, wilayaId: 1);

        var result = await ctrl.DeleteManagedUser(userId, default);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task DeleteAdmin_OutsideScope_ReturnsForbid()
    {
        using var db = CreateDb();
        db.Communes.AddRange(
            new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" },
            new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other daira" });
        var callerId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = callerId,
            Username = "caller",
            Email = "caller@test.com",
            Name = "Caller",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.DairaAdmin,
            DairaId = 1,
        });
        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Username = "commune_user",
            Email = "cu@test.com",
            Name = "Commune User",
            Phone = TestData.DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 99,
        });
        await db.SaveChangesAsync();
        var ctrl = CreateController(db);
        AuthTestHelper.SetUser(ctrl, callerId, UserRoles.DairaAdmin, dairaId: 1);

        var result = await ctrl.DeleteManagedUser(userId, default);

        Assert.IsType<ForbidResult>(result);
        Assert.NotNull(await db.Users.FindAsync(userId));
    }
}
