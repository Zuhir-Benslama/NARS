using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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

    private static AdminUserController CreateController(AppDbContext db, IDbContextFactory<AppDbContext>? factory = null)
    {
        var authSvc = new UserAuthorizationService(db, Mock.Of<IRefreshTokenService>(), Mock.Of<IFeatureCleanupService>(), Mock.Of<IDateTimeProvider>(), Mock.Of<ISecurityStampCache>());
        return new(Mock.Of<ILogger<AdminUserController>>(),
            authSvc,
            new UserCreationService(factory ?? new TestDbContextFactory(db), authSvc, Mock.Of<ILogger<UserCreationService>>()),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    // ─── CreateAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAdmin_NationalToWilaya_Returns201()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory);
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
    }

    [Fact]
    public async Task CreateAdmin_DuplicateUsername_Returns409()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            await SeedData.CreateUserAsync(db, UserRoles.WilayaAdmin, wilayaId: 1, username: "existing");
            var ctrl = CreateController(db, factory);
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
    }

    [Fact]
    public async Task CreateAdmin_DisallowedRolePair_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory);
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
    }

    [Fact]
    public async Task CreateAdmin_DairaAdminOutsideScope_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            db.Communes.Add(new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other" });
            await db.SaveChangesAsync();
            var ctrl = CreateController(db, factory);
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
    }

    // ─── GetManageableUsers ─────────────────────────────────────────────

    [Fact]
    public async Task GetManageableUsers_NationalAdmin_ReturnsWilayaAdmins()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            await SeedData.CreateUserAsync(db, UserRoles.WilayaAdmin, wilayaId: 1, username: "wa1");
            await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 1, username: "cu1");
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

            var result = await ctrl.GetManageableUsers(default);

            var ok = Assert.IsType<OkObjectResult>(result);
            var page = Assert.IsType<PagedResponse<AdminUserSummary>>(ok.Value);
            Assert.Single(page.Items);
            Assert.Equal("wa1", page.Items[0].Username);
            Assert.Equal(1, page.Total);
        }
    }

    [Fact]
    public async Task GetManageableUsers_NegativeSkip_IsClampedToZero()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            await SeedData.CreateUserAsync(db, UserRoles.WilayaAdmin, wilayaId: 1, username: "wa1");
            await SeedData.CreateUserAsync(db, UserRoles.WilayaAdmin, wilayaId: 1, username: "wa2");
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

            // With take=1, an un-clamped skip of -10 would return the empty slice; the
            // clamped skip of 0 must return the first user in username order.
            var result = await ctrl.GetManageableUsers(-10, 1, default);

            var ok = Assert.IsType<OkObjectResult>(result);
            var page = Assert.IsType<PagedResponse<AdminUserSummary>>(ok.Value);
            var user = Assert.Single(page.Items);
            Assert.Equal("wa1", user.Username);
        }
    }

    // ─── UpdateAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateAdmin_UserNotFound_Returns404()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

            var result = await ctrl.UpdateManagedUser(
                Guid.NewGuid(),
                new UpdateAdminRequest(Name: "Updated", Email: null, Phone: null,
                    Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
                default);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(404, problem.StatusCode);
        }
    }

    [Fact]
    public async Task UpdateAdmin_ValidNameUpdate_ReturnsOk()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var userId = Guid.NewGuid();
            await SeedData.CreateUserAsync(db, UserRoles.WilayaAdmin, wilayaId: 1, id: userId);
            var ctrl = CreateController(db, factory);
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
    }

    [Fact]
    public async Task UpdateAdmin_DairaAdminReassignsCommuneUserOutsideDaira_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            db.Communes.AddRange(
                new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" },
                new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other daira" });
            var caller = await SeedData.CreateUserAsync(db, UserRoles.DairaAdmin, dairaId: 1);
            var target = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 5);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, caller.Id, UserRoles.DairaAdmin, dairaId: 1);

            var result = await ctrl.UpdateManagedUser(
                target.Id,
                new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                    Role: null, CommuneId: 99, DairaId: null, WilayaId: null,
                    Password: TestData.DefaultPassword),
                default);

            Assert.IsType<ForbidResult>(result);
            Assert.Equal(5, (await db.Users.FindAsync(target.Id))!.CommuneId);
        }
    }

    [Fact]
    public async Task UpdateAdmin_DairaAdminReassignsCommuneUserWithinDaira_ReturnsOk()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            db.Communes.AddRange(
                new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "Commune A" },
                new Commune { CommuneId = 6, DairaId = 1, CommuneFr = "Commune B" });
            var caller = await SeedData.CreateUserAsync(db, UserRoles.DairaAdmin, dairaId: 1);
            var target = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 5);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, caller.Id, UserRoles.DairaAdmin, dairaId: 1);

            var result = await ctrl.UpdateManagedUser(
                target.Id,
                new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                    Role: null, CommuneId: 6, DairaId: null, WilayaId: null,
                    Password: TestData.DefaultPassword),
                default);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<ApiResponse>(ok.Value);
            Assert.True(response.Success);
            Assert.Equal(6, (await db.Users.FindAsync(target.Id))!.CommuneId);
        }
    }

    [Fact]
    public async Task UpdateAdmin_CommuneUserMovesFieldWorkerOutsideCommune_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var caller = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 1);
            var target = await SeedData.CreateUserAsync(db, UserRoles.FieldWorker, communeId: 1);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, caller.Id, UserRoles.CommuneUser, communeId: 1);

            var result = await ctrl.UpdateManagedUser(
                target.Id,
                new UpdateAdminRequest(Name: null, Email: null, Phone: null,
                    Role: null, CommuneId: 2, DairaId: null, WilayaId: null,
                    Password: TestData.DefaultPassword),
                default);

            Assert.IsType<ForbidResult>(result);
            Assert.Equal(1, (await db.Users.FindAsync(target.Id))!.CommuneId);
        }
    }

    [Fact]
    public async Task UpdateAdmin_ProfileOnlyEditOutsideScope_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            db.Communes.AddRange(
                new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" },
                new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other daira" });
            var caller = await SeedData.CreateUserAsync(db, UserRoles.DairaAdmin, dairaId: 1);
            var target = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 99);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, caller.Id, UserRoles.DairaAdmin, dairaId: 1);

            var result = await ctrl.UpdateManagedUser(
                target.Id,
                new UpdateAdminRequest(Name: "Sneaky", Email: null, Phone: null,
                    Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
                default);

            Assert.IsType<ForbidResult>(result);
            Assert.Equal(target.Name, (await db.Users.FindAsync(target.Id))!.Name);
        }
    }

    [Fact]
    public async Task UpdateAdmin_ProfileOnlyEditWithinScope_ReturnsOk()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            db.Communes.Add(new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" });
            var caller = await SeedData.CreateUserAsync(db, UserRoles.DairaAdmin, dairaId: 1);
            var target = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 5);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, caller.Id, UserRoles.DairaAdmin, dairaId: 1);

            var result = await ctrl.UpdateManagedUser(
                target.Id,
                new UpdateAdminRequest(Name: "Updated", Email: null, Phone: null,
                    Role: null, CommuneId: null, DairaId: null, WilayaId: null, Password: null),
                default);

            var ok = Assert.IsType<OkObjectResult>(result);
            var response = Assert.IsType<ApiResponse>(ok.Value);
            Assert.True(response.Success);
            Assert.Equal("Updated", (await db.Users.FindAsync(target.Id))!.Name);
        }
    }

    // ─── DeleteAdmin ────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAdmin_UserNotFound_Returns404()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.NationalAdmin);

            var result = await ctrl.DeleteManagedUser(Guid.NewGuid(), default);

            var problem = Assert.IsType<ObjectResult>(result);
            Assert.Equal(404, problem.StatusCode);
        }
    }

    // DeleteAdmin_Valid_ReturnsNoContent and DeleteAdmin_WithinScope_ReturnsNoContent
    // live in AdminControllerServiceTests (PostgreSQL-backed) because the InMemory
    // provider does not support ExecuteDeleteAsync/ExecuteUpdateAsync used by
    // UserAuthorizationService.DeleteUserAsync.

    [Fact]
    public async Task DeleteAdmin_ForbiddenRoleHierarchy_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            var target = await SeedData.CreateUserAsync(db, UserRoles.NationalAdmin);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, Guid.NewGuid(), UserRoles.WilayaAdmin, wilayaId: 1);

            var result = await ctrl.DeleteManagedUser(target.Id, default);

            Assert.IsType<ForbidResult>(result);
        }
    }

    [Fact]
    public async Task DeleteAdmin_OutsideScope_ReturnsForbid()
    {
        var (db, factory) = CreateInMemoryDbPair("AdminUserTest");
        await using (db)
        {
            db.Communes.AddRange(
                new Commune { CommuneId = 5, DairaId = 1, CommuneFr = "In daira" },
                new Commune { CommuneId = 99, DairaId = 99, CommuneFr = "Other daira" });
            var caller = await SeedData.CreateUserAsync(db, UserRoles.DairaAdmin, dairaId: 1);
            var target = await SeedData.CreateUserAsync(db, UserRoles.CommuneUser, communeId: 99);
            var ctrl = CreateController(db, factory);
            AuthTestHelper.SetUser(ctrl, caller.Id, UserRoles.DairaAdmin, dairaId: 1);

            var result = await ctrl.DeleteManagedUser(target.Id, default);

            Assert.IsType<ForbidResult>(result);
            Assert.NotNull(await db.Users.FindAsync(target.Id));
        }
    }
}
