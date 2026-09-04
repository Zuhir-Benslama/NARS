using System.Security.Claims;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
public class AuthControllerServiceTests(NarsDatabaseFixture fixture) : ServiceTestBase(fixture)
{

    protected override async Task SeedAsync()
    {
        await SeedReferenceDataAsync();
    }

    [Fact]
    public async Task AuthorizedAdminSignup_ValidRequest_CreatesUser()
    {
        await CreateAdminAsync(
            username: "daira_admin_1",
            role: UserRoles.DairaAdmin,
            dairaId: 1);

        var controller = CreateSignupController();
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        controller.ControllerContext.HttpContext.Request.Headers["X-Admin-Signup"] = AdminSignupToken;
        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "daira_admin_1",
            AdminPassword: DefaultPassword,
            Name: "Integration Test User",
            Email: "integration@test.com",
            Phone: DefaultPhone,
            Username: "integration_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ), AdminSignupToken);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);

        var user = await Db.Users.FirstOrDefaultAsync(u => u.Username == "integration_user");
        Assert.NotNull(user);
        Assert.Equal("integration@test.com", user.Email);
        Assert.Equal(1, user.CommuneId);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateUsername_Returns409()
    {
        await CreateAdminAsync(
            username: "daira_admin_1",
            role: UserRoles.DairaAdmin,
            dairaId: 1);

        var first = CreateSignupController();
        first.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        first.ControllerContext.HttpContext.Request.Headers["X-Admin-Signup"] = AdminSignupToken;
        await first.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "daira_admin_1",
            AdminPassword: DefaultPassword,
            Name: "User One",
            Email: "one@test.com",
            Phone: DefaultPhone,
            Username: "duplicate_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ), AdminSignupToken);

        var second = CreateSignupController();
        second.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        second.ControllerContext.HttpContext.Request.Headers["X-Admin-Signup"] = AdminSignupToken;
        var result = await second.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "daira_admin_1",
            AdminPassword: DefaultPassword,
            Name: "User Two",
            Email: "two@test.com",
            Phone: DefaultPhone,
            Username: "duplicate_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ), AdminSignupToken);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_RaceCondition_OneSucceedsOneConflicts()
    {
        // Seed the authorizing admin
        await CreateAdminAsync(
            username: "race_admin",
            role: UserRoles.DairaAdmin,
            dairaId: 1);

        // Two independent contexts + controllers simulating concurrent requests
        await using var db1 = Fixture.CreateDbContext();
        var factory1 = Fixture.CreateDbContextFactory();
        var ctrl1 = AuthTestHelper.CreateAdminSignupController(db1, factory1);
        ctrl1.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        ctrl1.ControllerContext.HttpContext.Request.Headers["X-Admin-Signup"] = TestData.AdminSignupToken;
        await using var db2 = Fixture.CreateDbContext();
        var factory2 = Fixture.CreateDbContextFactory();
        var ctrl2 = AuthTestHelper.CreateAdminSignupController(db2, factory2);
        ctrl2.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        ctrl2.ControllerContext.HttpContext.Request.Headers["X-Admin-Signup"] = TestData.AdminSignupToken;

        var request = new AuthorizedAdminSignupRequest(
            AdminUsername: "race_admin",
            AdminPassword: DefaultPassword,
            Name: "Race User",
            Email: "race@test.com",
            Phone: DefaultPhone,
            Username: "race_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        );

        // Fire both concurrently — both pass the SELECT uniqueness check,
        // then one INSERT succeeds while the other hits the unique constraint.
        // INVARIANT: this test only stays deterministic because the DB unique
        // index on (username) guarantees exactly one 201 + one 409 regardless
        // of whether the requests actually overlap. If the uniqueness check is
        // ever cached or wrapped so the constraint error is swallowed, this
        // becomes flaky — the 409 must come from the unique index.
        var results = await Task.WhenAll(
            ctrl1.AuthorizedAdminSignup(request, TestData.AdminSignupToken, CancellationToken.None),
            ctrl2.AuthorizedAdminSignup(request, TestData.AdminSignupToken, CancellationToken.None)
        );

        var statusCodes = results
            .Select(r => (r as ObjectResult)?.StatusCode ?? 0)
            .OrderBy(x => x)
            .ToList();

        Assert.Contains(201, statusCodes);
        Assert.Contains(409, statusCodes);

        // Exactly one user persisted
        await using var verifyDb = Fixture.CreateDbContext();
        var userCount = await verifyDb.Users.CountAsync(u => u.Username == "race_user");
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task SignIn_CorrectCredentials_ReturnsTokens()
    {
        var user = await CreateCommuneUserAsync(
            username: "signin_user",
            communeId: 1);

        var controller = CreateSignInController();
        var result = await controller.SignIn(new SignInRequest(
            Username: "signin_user",
            Password: DefaultPassword
        ));

        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<SignInResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal("signin_user", response.User.Username);
        Assert.Equal(UserRoles.CommuneUser, response.User.Role);

        // Auth cookies must be set on the response.
        var setCookie = controller.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains("access_token=", setCookie, StringComparison.Ordinal);
        Assert.Contains("refresh_token=", setCookie, StringComparison.Ordinal);

        // The refresh token must be persisted (and usable for rotation).
        await using var verifyDb = Fixture.CreateDbContext();
        var stored = await verifyDb.RefreshTokens.AsNoTracking()
            .FirstOrDefaultAsync(rt => rt.UserId == user.Id);
        Assert.NotNull(stored);
        Assert.False(stored.Revoked);
        Assert.True(stored.ExpiresAt > FixedUtcNow);
    }

    [Fact]
    public async Task SignIn_WrongPassword_Returns401()
    {
        await CreateCommuneUserAsync(
            username: "wrong_pass",
            communeId: 1);

        var controller = CreateSignInController();
        var result = await controller.SignIn(new SignInRequest(
            Username: "wrong_pass",
            Password: "WrongPass1!"
        ));

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public async Task Logout_RevokesRefreshTokens()
    {
        var user = await CreateCommuneUserAsync(
            username: "logout_user",
            communeId: 1);

        var signInController = CreateSignInController();
        await signInController.SignIn(new SignInRequest(
            Username: "logout_user",
            Password: DefaultPassword
        ));

        await using var db = Fixture.CreateDbContext();
        var tokenCount = await db.RefreshTokens.AsNoTracking().CountAsync(rt => rt.UserId == user.Id && !rt.Revoked);
        Assert.Equal(1, tokenCount);

        var claims = new List<Claim> { new(ClaimNames.UserId, user.Id.ToString()) };
        var httpContext = CreateHttpContext(claims);
        var logoutController = AuthTestHelper.CreateAuthController(db);
        logoutController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await logoutController.Logout();
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var revokedCount = await db.RefreshTokens.AsNoTracking().CountAsync(rt => rt.UserId == user.Id && rt.Revoked);
        Assert.Equal(1, revokedCount);
    }

    [Fact]
    public async Task CurrentUser_WithValidToken_ReturnsUserData()
    {
        var user = await CreateCommuneUserAsync(
            username: "current_user",
            communeId: 1);

        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, user.Id.ToString()),
            new(ClaimNames.CommuneId, "1"),
        };
        var httpContext = CreateHttpContext(claims);
        var currentController = CreateController();
        currentController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await currentController.CurrentUser();
        var okResult = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<UserInfoWithLocation>(okResult.Value);
        Assert.Equal(user.Id.ToString(), payload.Id);
        Assert.Equal("current_user", payload.Username);
        Assert.Equal(UserRoles.CommuneUser, payload.Role);
        Assert.Equal("current_user@test.com", payload.Email);
    }

    private AuthController CreateController() => AuthTestHelper.CreateAuthController(Db);

    private AdminSignupController CreateSignupController() => AuthTestHelper.CreateAdminSignupController(Db, Fixture.CreateDbContextFactory());

    private async Task SeedReferenceDataAsync() => await SeedData.SeedBasicLocationsAsync(Db);

    private async Task<User> CreateAdminAsync(
        string username,
        string role,
        int? dairaId = null,
        int? wilayaId = null)
    {
        var user = await SeedData.CreateUserAsync(Db, role, dairaId: dairaId, wilayaId: wilayaId,
            name: $"Admin {username}", username: username, email: $"{username}@test.com");
        return user;
    }

    private async Task<User> CreateCommuneUserAsync(string username, int communeId)
    {
        var user = await SeedData.CreateUserAsync(Db, UserRoles.CommuneUser, communeId: communeId,
            name: $"User {username}", username: username, email: $"{username}@test.com");
        return user;
    }

    private static DefaultHttpContext CreateHttpContext(List<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        return httpContext;
    }

    private AuthController CreateSignInController()
    {
        var ctrl = AuthTestHelper.CreateAuthController(Db);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return ctrl;
    }
}
