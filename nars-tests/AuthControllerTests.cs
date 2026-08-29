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

    private static AuthController CreateController(AppDbContext db) =>
        AttachContext(AuthTestHelper.CreateAuthController(db));

    private static AdminSignupController CreateSignupController(AppDbContext db, IDbContextFactory<AppDbContext>? factory = null) =>
        AttachContext(AuthTestHelper.CreateAdminSignupController(db, factory));

    private static T AttachContext<T>(T controller) where T : ControllerBase
    {
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return controller;
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
        var (db, factory) = CreateInMemoryDbPair("AuthTest");
        await using (db)
        {
            await SeedLocationDataAsync(db);
            await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

            var controller = CreateSignupController(db, factory);

            var result = await controller.AuthorizedAdminSignup(
                ValidAdminSignup(), signupToken: AdminSignupToken);

            var statusCodeResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(201, statusCodeResult.StatusCode);
        }
    }

    [Fact]
    public async Task AuthorizedAdminSignup_WeakPassword_Returns400()
    {
        var (db, factory) = CreateInMemoryDbPair("AuthTest");
        await using (db)
        {
            await SeedLocationDataAsync(db);
            await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

            var controller = CreateSignupController(db, factory);

            var result = await controller.AuthorizedAdminSignup(
                ValidAdminSignup(password: "weak"), signupToken: AdminSignupToken);

            var objResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(400, objResult.StatusCode);
        }
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateUsername_Returns409()
    {
        var (db, factory) = CreateInMemoryDbPair("AuthTest");
        await using (db)
        {
            await SeedLocationDataAsync(db);
            await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);
            await db.Users.AddAsync(new User
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                Email = "existing@example.com",
                Phone = TestData.DefaultPhone,
                Username = "existinguser",
                PasswordHash = DefaultPasswordHash,
                SecurityStamp = User.GenerateSecurityStamp(),
                Role = UserRoles.CommuneUser,
                CommuneId = CommuneId1,
            });
            await db.SaveChangesAsync();

            var controller = CreateSignupController(db, factory);

            var result = await controller.AuthorizedAdminSignup(
                ValidAdminSignup(username: "existinguser", email: "new@example.com"),
                signupToken: AdminSignupToken);

            var conflict = Assert.IsType<ObjectResult>(result);
            Assert.Equal(409, conflict.StatusCode);
        }
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateEmail_Returns409()
    {
        var (db, factory) = CreateInMemoryDbPair("AuthTest");
        await using (db)
        {
            await SeedLocationDataAsync(db);
            await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);
            await db.Users.AddAsync(new User
            {
                Id = Guid.NewGuid(),
                Name = "Existing",
                Email = "dupe@example.com",
                Phone = TestData.DefaultPhone,
                Username = "existinguser",
                PasswordHash = DefaultPasswordHash,
                SecurityStamp = User.GenerateSecurityStamp(),
                Role = UserRoles.CommuneUser,
                CommuneId = CommuneId1,
            });
            await db.SaveChangesAsync();

            var controller = CreateSignupController(db, factory);

            var result = await controller.AuthorizedAdminSignup(
                ValidAdminSignup(username: "newuser", email: "dupe@example.com"),
                signupToken: AdminSignupToken);

            var conflict = Assert.IsType<ObjectResult>(result);
            Assert.Equal(409, conflict.StatusCode);
        }
    }

    [Fact]
    public async Task AuthorizedAdminSignup_CommuneOutsideAdminScope_Returns403()
    {
        var (db, factory) = CreateInMemoryDbPair("AuthTest");
        await using (db)
        {
            await SeedLocationDataAsync(db);
            await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

            var controller = CreateSignupController(db, factory);

            var result = await controller.AuthorizedAdminSignup(
                ValidAdminSignup(communeId: CommuneId2), signupToken: AdminSignupToken);

            Assert.IsType<ForbidResult>(result);
        }
    }

    [Fact]
    public async Task AuthorizedAdminSignup_FieldWorker_ForbiddenForCommuneUserAdmin()
    {
        var (db, factory) = CreateInMemoryDbPair("AuthTest");
        await using (db)
        {
            await SeedLocationDataAsync(db);
            await SeedAdminAsync(db, username: "commune_admin", role: UserRoles.CommuneUser, communeId: CommuneId1);

            var controller = CreateSignupController(db, factory);

            var result = await controller.AuthorizedAdminSignup(
                ValidAdminSignup(
                    username: "fieldworker", role: UserRoles.FieldWorker,
                    communeId: CommuneId2, adminUsername: "commune_admin", name: "Field Worker"),
                signupToken: AdminSignupToken);

            Assert.IsType<ForbidResult>(result);
            Assert.False(await db.Users.AnyAsync(u => u.Username == "fieldworker"));
        }
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
            PasswordHash = DefaultPasswordHash,
            SecurityStamp = User.GenerateSecurityStamp(),
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
