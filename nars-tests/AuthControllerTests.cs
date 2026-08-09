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

namespace NarsApi.Tests;

public class AuthControllerTests
{
    private static AppDbContext CreateDb() => CreateInMemoryDb("AuthTest");

    private static JwtService CreateJwtService(IDateTimeProvider? timeProvider = null)
    {
        timeProvider ??= Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var jwtOptions = DefaultJwtOptions;
        return new JwtService(
            AuthTestHelper.TestJwtSecret,
            null,
            null,
            jwtOptions,
            Mock.Of<ILogger<JwtService>>(),
            timeProvider);
    }

    private static AuthController CreateController(AppDbContext db)
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var lockoutOptions = Options.Create(new AccountLockoutOptions());
        var jwtService = CreateJwtService(timeProvider);
        var refreshService = new RefreshTokenService(db, jwtService, DefaultJwtOptions, timeProvider);
        return new AuthController(
            refreshService,
            jwtService,
            lockoutOptions,
            Options.Create(new AdminSignupOptions { SignupToken = TestData.AdminSignupToken }),
            Mock.Of<ILogger<AuthController>>(),
            timeProvider,
            new UserAuthorizationService(db, refreshService, timeProvider),
            new UserCreationService(db, new UserAuthorizationService(db, refreshService, timeProvider), Mock.Of<ILogger<UserCreationService>>()),
            Mock.Of<ILocationQueryService>(),
            Mock.Of<IWebHostEnvironment>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext(),
            }
        };
    }

    [Fact]
    public void SignUp_PublicEndpointIsDisabled_Returns410()
    {
        using var db = CreateDb();
        var controller = CreateController(db);

        var result = controller.SignUp();

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(410, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_ValidRequest_Returns201()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: TestData.DefaultPassword,
            Name: "Test User",
            Email: TestData.DefaultEmail,
            Phone: TestData.AltPhone,
            Username: "testuser",
            Password: TestData.AltPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: CommuneId1,
            DairaId: null,
            WilayaId: null
        ), signupToken: AdminSignupToken);

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_WeakPassword_Returns400()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: TestData.DefaultPassword,
            Name: "Test User",
            Email: TestData.DefaultEmail,
            Phone: AltPhone,
            Username: "testuser",
            Password: "weak",
            Role: UserRoles.CommuneUser,
            CommuneId: CommuneId1,
            DairaId: null,
            WilayaId: null
        ), signupToken: AdminSignupToken);

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateUsername_Returns409()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);
        await db.Users.AddAsync(new User
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            Email = "existing@example.com",
            Phone = TestData.DefaultPhone,
            Username = "existinguser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role = UserRoles.CommuneUser,
            CommuneId = CommuneId1,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: DefaultPassword,
            Name: "New User",
            Email: "new@example.com",
            Phone: AltPhone,
            Username: "existinguser",
            Password: AltPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: CommuneId1,
            DairaId: null,
            WilayaId: null
        ), signupToken: AdminSignupToken);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateEmail_Returns409()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);
        await db.Users.AddAsync(new User
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            Email = "dupe@example.com",
            Phone = TestData.DefaultPhone,
            Username = "existinguser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role = UserRoles.CommuneUser,
            CommuneId = CommuneId1,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: DefaultPassword,
            Name: "New User",
            Email: "dupe@example.com",
            Phone: AltPhone,
            Username: "newuser",
            Password: AltPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: CommuneId1,
            DairaId: null,
            WilayaId: null
        ), signupToken: AdminSignupToken);

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_CommuneOutsideAdminScope_Returns403()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: DefaultPassword,
            Name: "Test User",
            Email: TestData.DefaultEmail,
            Phone: AltPhone,
            Username: "testuser",
            Password: AltPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 2,
            DairaId: null,
            WilayaId: null
        ), signupToken: AdminSignupToken);

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_FieldWorker_ForbiddenForCommuneUserAdmin()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "commune_admin", role: UserRoles.CommuneUser, communeId: CommuneId1);

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "commune_admin",
            AdminPassword: DefaultPassword,
            Name: "Field Worker",
            Email: TestData.DefaultEmail,
            Phone: AltPhone,
            Username: "fieldworker",
            Password: AltPassword,
            Role: UserRoles.FieldWorker,
            CommuneId: 2,
            DairaId: null,
            WilayaId: null
        ), signupToken: AdminSignupToken);

        Assert.IsType<ForbidResult>(result);
        Assert.False(await db.Users.AnyAsync(u => u.Username == "fieldworker"));
    }

    [Fact]
    public async Task SignIn_WrongPassword_Returns401()
    {
        using var db = CreateDb();
        await SeedLocationDataAsync(db);
        await db.Users.AddAsync(new User
        {
            Id = Guid.NewGuid(),
            Name = "Test User",
            Email = TestData.DefaultEmail,
            Username = "testuser",
            Phone = TestData.DefaultPhone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role = UserRoles.CommuneUser,
            CommuneId = CommuneId1,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);

        var result = await controller.SignIn(new SignInRequest(
            Username: "testuser",
            Password: "WrongP@ss1"
        ));

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    [Fact]
    public async Task SignIn_UserNotFound_Returns401()
    {
        using var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.SignIn(new SignInRequest(
            Username: "nonexistent",
            Password: DefaultPassword
        ));

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    // Logout is tested in AuthControllerServiceTests.Logout_RevokesRefreshTokens
    // (real PostgreSQL supports ExecuteUpdateAsync; InMemory does not).

    [Fact]
    public async Task Logout_DeletesAuthCookiesWithMatchingOptions()
    {
        using var db = CreateDb();
        var controller = CreateController(db);
        // No user_id claim → token revocation is skipped (InMemory lacks
        // ExecuteUpdateAsync), isolating the cookie-deletion behavior under test.
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };

        var result = await controller.Logout();

        Assert.IsType<OkObjectResult>(result);
        var setCookie = controller.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(CookieNames.AccessToken + "=", setCookie);
        Assert.Contains(CookieNames.RefreshToken + "=", setCookie);
        Assert.Contains("expires=", setCookie);
        Assert.Contains("path=/", setCookie);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedLocationDataAsync(AppDbContext db) =>
        await SeedData.SeedExtendedLocationsAsync(db);

    private static async Task SeedAdminAsync(AppDbContext db, string username, string role, int? communeId = null, int? dairaId = null, int? wilayaId = null)
    {
        var user = await SeedData.CreateUserAsync(db, role, communeId: communeId, dairaId: dairaId, wilayaId: wilayaId, name: "Admin User");
        user.Username = username;
        await db.SaveChangesAsync();
    }
}
