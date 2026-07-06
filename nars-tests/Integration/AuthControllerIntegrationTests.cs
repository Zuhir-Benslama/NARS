using System.Security.Claims;
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

[Collection("PostgreSQL Integration")]
public class AuthControllerIntegrationTests : IAsyncLifetime
{
    private readonly NarsDatabaseFixture _fixture;
    private readonly AppDbContext _db;
    private readonly AuthController _controller;

    public AuthControllerIntegrationTests(NarsDatabaseFixture fixture)
    {
        _fixture = fixture;
        _db = fixture.CreateDbContext();
        _controller = CreateController(_db);
    }

    public async Task InitializeAsync() => await SeedReferenceDataAsync();

    public async Task DisposeAsync() => await _fixture.CleanTablesAsync();

    [Fact]
    public async Task AuthorizedAdminSignup_ValidRequest_CreatesUser()
    {
        await CreateAdminAsync(
            username: "daira_admin_1",
            role: UserRoles.DairaAdmin,
            password: DefaultPassword,
            dairaId: 1);

        var result = await _controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "daira_admin_1",
            AdminPassword: DefaultPassword,
            Name: "Integration Test User",
            Email: "integration@test.com",
            Phone: "0555999888",
            Username: "integration_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

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

        await _controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "daira_admin_1",
            AdminPassword: DefaultPassword,
            Name: "User One",
            Email: "one@test.com",
            Phone: "0555111222",
            Username: "duplicate_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        var result = await _controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "daira_admin_1",
            AdminPassword: DefaultPassword,
            Name: "User Two",
            Email: "two@test.com",
            Phone: "0555333444",
            Username: "duplicate_user",
            Password: DefaultPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        Assert.IsType<ConflictObjectResult>(result);
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
            Phone: "0555999888",
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
            ctrl1.AuthorizedAdminSignup(request, CancellationToken.None),
            ctrl2.AuthorizedAdminSignup(request, CancellationToken.None)
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

        Assert.IsType<UnauthorizedObjectResult>(result);
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

        var tokenCount = await _db.RefreshTokens.CountAsync(rt => rt.UserId == user.Id && !rt.Revoked);
        Assert.True(tokenCount > 0);

        var claims = new List<Claim> { new(ClaimNames.UserId, user.Id.ToString()) };
        var httpContext = CreateHttpContext(claims);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.Logout();
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        var revokedCount = await _db.RefreshTokens.CountAsync(rt => rt.UserId == user.Id && rt.Revoked);
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
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.CurrentUser();
        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.NotNull(okResult.Value);
    }

    private static AuthController CreateController(AppDbContext db)
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == DateTime.UtcNow);
        var jwtOpts = Options.Create(new JwtOptions { ExpiresInMinutes = 60, RefreshExpiresInDays = 30 });
        var jwt = new JwtService(
            AuthTestHelper.TestJwtSecret,
            null,
            null,
            jwtOpts,
            Mock.Of<ILogger<JwtService>>(),
            timeProvider);
        return new AuthController(
            db,
            jwt,
            jwtOpts,
            Options.Create(new AccountLockoutOptions()),
            Mock.Of<ILogger<AuthController>>(),
            timeProvider,
            new UserAuthorizationService(db));
    }

    private async Task SeedReferenceDataAsync() => await SeedData.SeedBasicLocationsAsync(_db);

    private async Task<User> CreateAdminAsync(
        string username,
        string role,
        string password,
        int? dairaId = null,
        int? wilayaId = null)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = $"Admin {username}",
            Email = $"{username}@test.com",
            Phone = DefaultPhone,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = role,
            DairaId = dairaId,
            WilayaId = wilayaId,
        };

        await _db.Users.AddAsync(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<User> CreateCommuneUserAsync(string username, string password, int communeId)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = $"User {username}",
            Email = $"{username}@test.com",
            Phone = DefaultPhone,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            Role = UserRoles.CommuneUser,
            CommuneId = communeId,
        };

        await _db.Users.AddAsync(user);
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
