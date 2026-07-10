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
        var jwtOptions = Options.Create(new JwtOptions { ExpiresInMinutes = 60, RefreshExpiresInDays = 30 });
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
        var jwtOptions = Options.Create(new JwtOptions { ExpiresInMinutes = 60, RefreshExpiresInDays = 30 });
        var lockoutOptions = Options.Create(new AccountLockoutOptions());
        return new AuthController(
            db,
            CreateJwtService(timeProvider),
            jwtOptions,
            lockoutOptions,
            Mock.Of<ILogger<AuthController>>(),
            timeProvider,
            new UserAuthorizationService(db),
            AuthTestHelper.CreateUserCreationMock(db),
            Mock.Of<IWebHostEnvironment>());
    }

    [Fact]
    public void SignUp_PublicEndpointIsDisabled_Returns410()
    {
        var db = CreateDb();
        var controller = CreateController(db);

        var result = controller.SignUp();

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(410, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_ValidRequest_Returns201()
    {
        var db = CreateDb();
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
            CommuneId: TestData.CommuneId1,
            DairaId: null,
            WilayaId: null
        ));

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_WeakPassword_Returns400()
    {
        var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: TestData.DefaultPassword,
            Name: "Test User",
            Email: "test@example.com",
            Phone: AltPhone,
            Username: "testuser",
            Password: "weak",
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        var objResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateUsername_Returns409()
    {
        var db = CreateDb();
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
            CommuneId = 1,
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
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateEmail_Returns409()
    {
        var db = CreateDb();
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
            CommuneId = 1,
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
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        var conflict = Assert.IsType<ObjectResult>(result);
        Assert.Equal(409, conflict.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_CommuneOutsideAdminScope_Returns403()
    {
        var db = CreateDb();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var controller = CreateController(db);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: DefaultPassword,
            Name: "Test User",
            Email: "test@example.com",
            Phone: AltPhone,
            Username: "testuser",
            Password: AltPassword,
            Role: UserRoles.CommuneUser,
            CommuneId: 2,
            DairaId: null,
            WilayaId: null
        ));

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task SignIn_WrongPassword_Returns401()
    {
        var db = CreateDb();
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
            CommuneId = 1,
        });
        await db.SaveChangesAsync();

        var controller = CreateController(db);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

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
        var db = CreateDb();
        var controller = CreateController(db);

        var result = await controller.SignIn(new SignInRequest(
            Username: "nonexistent",
            Password: DefaultPassword
        ));

        var unauthorized = Assert.IsType<ObjectResult>(result);
        Assert.Equal(401, unauthorized.StatusCode);
    }

    // Logout is tested in AuthControllerIntegrationTests.Logout_RevokesRefreshTokens
    // (real PostgreSQL supports ExecuteUpdateAsync; InMemory does not).

    private static async Task SeedLocationDataAsync(AppDbContext db) =>
        await SeedData.SeedExtendedLocationsAsync(db);

    private static async Task SeedAdminAsync(AppDbContext db, string username, string role, int? dairaId = null, int? wilayaId = null)
    {
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = "Admin User",
            Email = $"admin-{Guid.NewGuid():N}@test.com",
            Phone = TestData.DefaultPhone,
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(DefaultPassword),
            Role = role,
            DairaId = dairaId,
            WilayaId = wilayaId,
        });
        await db.SaveChangesAsync();
    }
}
