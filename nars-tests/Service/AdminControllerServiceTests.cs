using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests.Service;

[Collection(PostgreSqlCollection.CollectionName)]
[Trait("Category", "Service")]
public class AdminControllerServiceTests(NarsDatabaseFixture fixture) : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture = fixture;
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        await SeedData.SeedAdminLocationsAsync(_db);
    }

    public async Task DisposeAsync()
    {
        try { await _db.DisposeAsync(); }
        finally { await _fixture.CleanTablesAsync(); }
    }

    private AdminController CreateOverviewController()
    {
        var featureStats = new FeatureStatsService(_fixture.CreateDbContextFactory());
        return new AdminController(new AdminOverviewService(_db, featureStats), Mock.Of<IWebHostEnvironment>());
    }

    private AdminUserController CreateUserManagementController() => new(
            Mock.Of<Microsoft.Extensions.Logging.ILogger<AdminUserController>>(),
            new UserAuthorizationService(_db, Mock.Of<IRefreshTokenService>(), Mock.Of<IDateTimeProvider>()),
            new UserCreationService(_db, new UserAuthorizationService(_db, Mock.Of<IRefreshTokenService>(), Mock.Of<IDateTimeProvider>()),
                Mock.Of<Microsoft.Extensions.Logging.ILogger<UserCreationService>>()),
            Mock.Of<IWebHostEnvironment>());

    // ── CreateAdmin ─────────────────────────────────────────────────────

    [Fact]
    public async Task CreateAdmin_DairaAdminToCommuneUser_InOwnDaira_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.DairaAdmin, dairaId: 10);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(UserRoles.CommuneUser, communeId: CommuneId100);

        var result = await controller.CreateManagedUser(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.CommuneUser, createdUser.Role);
        Assert.Equal(CommuneId100, createdUser.CommuneId);
    }

    [Fact]
    public async Task CreateAdmin_WilayaAdminToDairaAdmin_InOwnWilaya_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(UserRoles.DairaAdmin, dairaId: DairaId10);

        var result = await controller.CreateManagedUser(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.DairaAdmin, createdUser.Role);
        Assert.Equal(10, createdUser.DairaId);
    }

    [Fact]
    public async Task CreateAdmin_NationalAdminToWilayaAdmin_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(UserRoles.WilayaAdmin, wilayaId: WilayaId2);

        var result = await controller.CreateManagedUser(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.WilayaAdmin, createdUser.Role);
        Assert.Equal(2, createdUser.WilayaId);
    }

    [Fact]
    public async Task CreateAdmin_DairaAdminToCommuneUser_OutsideOwnDaira_ReturnsForbid()
    {
        var creator = await CreateUserAsync(UserRoles.DairaAdmin, dairaId: 10);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(UserRoles.CommuneUser, communeId: CommuneId101);

        var result = await controller.CreateManagedUser(request);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task CreateAdmin_WilayaAdminToDairaAdmin_OutsideOwnWilaya_ReturnsForbid()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(UserRoles.DairaAdmin, dairaId: DairaId11);

        var result = await controller.CreateManagedUser(request);

        Assert.IsType<ForbidResult>(result);
    }

    [Theory]
    [MemberData(nameof(DisallowedRolePairs))]
    public async Task CreateAdmin_DisallowedRolePair_ReturnsForbid(string creatorRole, string targetRole)
    {
        var creator = await CreateUserAsync(
            creatorRole,
            dairaId: creatorRole == UserRoles.DairaAdmin ? 10 : null,
            wilayaId: creatorRole == UserRoles.WilayaAdmin ? 1 : null,
            communeId: creatorRole == UserRoles.CommuneUser ? CommuneId100 : null);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(targetRole, communeId: CommuneId100, dairaId: DairaId10, wilayaId: WilayaId1);

        var result = await controller.CreateManagedUser(request);

        Assert.IsType<ForbidResult>(result);
    }

    public static TheoryData<string, string> DisallowedRolePairs() => new()
    {
            { UserRoles.NationalAdmin, UserRoles.NationalAdmin },
            { UserRoles.NationalAdmin, UserRoles.DairaAdmin },
            { UserRoles.NationalAdmin, UserRoles.CommuneUser },
            { UserRoles.WilayaAdmin, UserRoles.WilayaAdmin },
            { UserRoles.WilayaAdmin, UserRoles.CommuneUser },
            { UserRoles.WilayaAdmin, UserRoles.NationalAdmin },
            { UserRoles.DairaAdmin, UserRoles.DairaAdmin },
            { UserRoles.DairaAdmin, UserRoles.WilayaAdmin },
            { UserRoles.DairaAdmin, UserRoles.NationalAdmin },
            { UserRoles.CommuneUser, UserRoles.CommuneUser },
            { UserRoles.CommuneUser, UserRoles.DairaAdmin },
            { UserRoles.CommuneUser, UserRoles.WilayaAdmin },
            { UserRoles.CommuneUser, UserRoles.NationalAdmin },
        };

    // ── CommuneUser → FieldWorker ─────────────────────────────────────────────

    [Fact]
    public async Task CreateAdmin_CommuneUserToFieldWorker_Returns201()
    {
        var creator = await CreateUserAsync(UserRoles.CommuneUser, communeId: CommuneId100);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);
        var request = BuildRequest(UserRoles.FieldWorker, communeId: null);

        var result = await controller.CreateManagedUser(request);

        var created = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, created.StatusCode);

        var createdUser = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username);
        Assert.NotNull(createdUser);
        Assert.Equal(UserRoles.FieldWorker, createdUser.Role);
        Assert.Equal(CommuneId100, createdUser.CommuneId);
    }

    // ── DeleteManagedUser ──────────────────────────────────────────────────────
    // These tests are PostgreSQL-backed (unlike the InMemory variants) because
    // UserAuthorizationService.DeleteUserAsync uses ExecuteDeleteAsync /
    // ExecuteUpdateAsync, which the InMemory provider does not support.

    [Fact]
    public async Task DeleteAdmin_Valid_ReturnsNoContent()
    {
        var target = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.DeleteManagedUser(target.Id, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.Users.FindAsync(target.Id));
    }

    [Fact]
    public async Task DeleteAdmin_WithinScope_ReturnsNoContent()
    {
        var creator = await CreateUserAsync(UserRoles.DairaAdmin, dairaId: 10);
        var target = await CreateUserAsync(UserRoles.CommuneUser, communeId: CommuneId100);
        var controller = CreateUserManagementController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.DeleteManagedUser(target.Id, default);

        Assert.IsType<NoContentResult>(result);
        Assert.Null(await _db.Users.FindAsync(target.Id));
    }

    // ── Overview ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Overview_NationalAdmin_ReturnsNationalOverview()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.Overview();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<NationalOverviewResponse>(okResult.Value);
        Assert.Equal(2, payload.Wilayas.Count);
    }

    [Fact]
    public async Task Overview_WilayaAdmin_ReturnsWilayaReport()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 2);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.Overview();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<WilayaReport>(okResult.Value);
        Assert.Equal(2, report.WilayaId);
        Assert.Single(report.Dairas);
        Assert.Equal(11, report.Dairas[0].DairaId);
    }

    [Fact]
    public async Task Overview_DairaAdmin_ReturnsDairaReport()
    {
        var creator = await CreateUserAsync(UserRoles.DairaAdmin, dairaId: 10);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.Overview();

        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<DairaReport>(okResult.Value);
        Assert.Equal(10, report.DairaId);
        Assert.Single(report.Communes);
        Assert.Equal(CommuneId100, report.Communes[0].CommuneId);
    }

    [Fact]
    public async Task Overview_CommuneUser_ReturnsForbid()
    {
        var creator = await CreateUserAsync(UserRoles.CommuneUser, communeId: CommuneId100);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.Overview();

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Overview_FieldWorker_ReturnsForbid()
    {
        var creator = await CreateUserAsync(UserRoles.FieldWorker, communeId: CommuneId100);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.Overview();

        Assert.IsType<ForbidResult>(result);
    }

    // ── Wilaya drill-down ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetWilaya_NationalAdmin_ReturnsWilayaReport()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.GetWilaya(2);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<WilayaReport>(okResult.Value);
        Assert.Equal(2, report.WilayaId);
        Assert.Single(report.Dairas);
        Assert.Equal(11, report.Dairas[0].DairaId);
    }

    [Fact]
    public async Task GetWilaya_NationalAdmin_UnknownId_ReturnsNotFound()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.GetWilaya(NonExistentId);

        var notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    // ── Daira drill-down ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetDaira_WilayaAdmin_OwnDaira_ReturnsDairaReport()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.GetDaira(10);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<DairaReport>(okResult.Value);
        Assert.Equal(10, report.DairaId);
        Assert.Single(report.Communes);
    }

    [Fact]
    public async Task GetDaira_WilayaAdmin_WrongWilaya_ReturnsNotFound()
    {
        var creator = await CreateUserAsync(UserRoles.WilayaAdmin, wilayaId: 1);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.GetDaira(11);

        var problem = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, problem.StatusCode);
    }

    [Fact]
    public async Task GetDaira_NationalAdmin_ReturnsDairaReport()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.GetDaira(11);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<DairaReport>(okResult.Value);
        Assert.Equal(11, report.DairaId);
        Assert.Single(report.Communes);
    }

    [Fact]
    public async Task GetDaira_NationalAdmin_UnknownId_ReturnsNotFound()
    {
        var creator = await CreateUserAsync(UserRoles.NationalAdmin);
        var controller = CreateOverviewController();
        AuthTestHelper.SetUser(controller, creator);

        var result = await controller.GetDaira(NonExistentId);

        var notFound = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, notFound.StatusCode);
    }

    private static CreateAdminRequest BuildRequest(
        string role,
        int? communeId = null,
        int? dairaId = null,
        int? wilayaId = null)
    {
        var suffix = Guid.NewGuid().ToString("N");
        return new CreateAdminRequest(
            Name: $"Role Test {suffix[..8]}",
            Email: $"role-{suffix}@test.com",
            Phone: DefaultPhone,
            Username: $"role_{suffix[..12]}",
            Password: DefaultPassword,
            Role: role,
            CommuneId: communeId,
            DairaId: dairaId,
            WilayaId: wilayaId);
    }

    private async Task<User> CreateUserAsync(
        string role,
        int? communeId = null,
        int? dairaId = null,
        int? wilayaId = null) => await SeedData.CreateUserAsync(_db, role, communeId, dairaId, wilayaId, name: $"Creator {Guid.NewGuid().ToString("N")[..8]}");
}
