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

namespace NarsApi.Tests.Integration;

[Collection(PostgreSqlCollection.CollectionName)]
public class AuthControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private AppDbContext _db = null!;

    public AuthControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        _db = _fixture.CreateDbContext();
        await SeedReferenceDataAsync();
    }

    public async Task DisposeAsync()
    {
        try { await _db.DisposeAsync(); }
        finally { await _fixture.CleanTablesAsync(); }
    }

    [Fact]
    public async Task AuthorizedAdminSignup_ValidRequest_CreatesUser()
    {
        await CreateAdminAsync(
            username: "daira_admin_1",
            role: UserRoles.DairaAdmin,
            password: DefaultPassword,
            dairaId: 1);

        var controller = CreateController();
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
        ), signupToken: AdminSignupToken);

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == "integration_user");
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
            password: DefaultPassword,
            dairaId: 1);

        var first = CreateController();
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
        ), signupToken: AdminSignupToken);

        var second = CreateController();
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
        ), signupToken: AdminSignupToken);

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
            password: DefaultPassword,
            dairaId: 1);

        // Two independent contexts + controllers simulating concurrent requests
        var db1 = _fixture.CreateDbContext();
        var ctrl1 = CreateController(db1);
        var db2 = _fixture.CreateDbContext();
        var ctrl2 = CreateController(db2);

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
        var results = await Task.WhenAll(
            ctrl1.AuthorizedAdminSignup(request, signupToken: TestData.AdminSignupToken, CancellationToken.None),
            ctrl2.AuthorizedAdminSignup(request, signupToken: TestData.AdminSignupToken, CancellationToken.None)
        );

        var statusCodes = results
            .Select(r => (r as ObjectResult)?.StatusCode ?? 0)
            .OrderBy(x => x)
            .ToList();

        Assert.Contains(201, statusCodes);
        Assert.Contains(409, statusCodes);

        // Exactly one user persisted
        await using var verifyDb = _fixture.CreateDbContext();
        var userCount = await verifyDb.Users.CountAsync(u => u.Username == "race_user");
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task SignIn_CorrectCredentials_ReturnsTokens()
    {
        await CreateCommuneUserAsync(
            username: "signin_user",
            password: DefaultPassword,
            communeId: 1);

        var controller = CreateSignInController();
        var result = await controller.SignIn(new SignInRequest(
            Username: "signin_user",
            Password: DefaultPassword
        ));

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task SignIn_WrongPassword_Returns401()
    {
        await CreateCommuneUserAsync(
            username: "wrong_pass",
            password: DefaultPassword,
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
            password: DefaultPassword,
            communeId: 1);

        var signInController = CreateSignInController();
        await signInController.SignIn(new SignInRequest(
            Username: "logout_user",
            Password: DefaultPassword
        ));

        await using var db = _fixture.CreateDbContext();
        var tokenCount = await db.RefreshTokens.AsNoTracking().CountAsync(rt => rt.UserId == user.Id && !rt.Revoked);
        Assert.True(tokenCount > 0);

        var claims = new List<Claim> { new(ClaimNames.UserId, user.Id.ToString()) };
        var httpContext = CreateHttpContext(claims);
        var logoutController = CreateController(db);
        logoutController.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await logoutController.Logout();
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var revokedCount = await db.RefreshTokens.AsNoTracking().CountAsync(rt => rt.UserId == user.Id && rt.Revoked);
        Assert.True(revokedCount > 0);
    }

    [Fact]
    public async Task CurrentUser_WithValidToken_ReturnsUserData()
    {
        var user = await CreateCommuneUserAsync(
            username: "current_user",
            password: DefaultPassword,
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
        Assert.NotNull(okResult.Value);
    }

    private AuthController CreateController() => CreateController(_db);

    private static AuthController CreateController(AppDbContext db)
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var jwtOpts = DefaultJwtOptions;
        var jwt = new JwtService(
            AuthTestHelper.TestJwtSecret,
            null,
            null,
            jwtOpts,
            Mock.Of<ILogger<JwtService>>(),
            timeProvider);
        var refreshService = new RefreshTokenService(db, jwt, jwtOpts, timeProvider);
        return new AuthController(
            refreshService,
            jwt,
            Options.Create(new AccountLockoutOptions()),
            Options.Create(new AdminSignupOptions { SignupToken = TestData.AdminSignupToken }),
            Mock.Of<ILogger<AuthController>>(),
            timeProvider,
            new UserAuthorizationService(db),
            AuthTestHelper.CreateUserCreationMock(db),
            Mock.Of<ILocationQueryService>(),
            Mock.Of<IWebHostEnvironment>());
    }

    private async Task SeedReferenceDataAsync() => await SeedData.SeedBasicLocationsAsync(_db);

    private async Task<User> CreateAdminAsync(
        string username,
        string role,
        string password,
        int? dairaId = null,
        int? wilayaId = null)
    {
        var user = await SeedData.CreateUserAsync(_db, role, dairaId: dairaId, wilayaId: wilayaId, name: $"Admin {username}");
        // Override username since CreateUserAsync generates one
        user.Username = username;
        user.Email = $"{username}@test.com";
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<User> CreateCommuneUserAsync(string username, string password, int communeId)
    {
        var user = await SeedData.CreateUserAsync(_db, UserRoles.CommuneUser, communeId: communeId, name: $"User {username}");
        user.Username = username;
        user.Email = $"{username}@test.com";
        await _db.SaveChangesAsync();
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
        var ctrl = CreateController(_db);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return ctrl;
    }
}
