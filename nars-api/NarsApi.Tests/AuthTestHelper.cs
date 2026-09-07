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
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public static class AuthTestHelper
{
    /// <summary>Shared test JWT secret key (min 32 chars for HMAC-SHA256).</summary>
    public const string TestJwtSecret = "test-secret-key-that-is-at-least-32-chars-long!!";

    /// <summary>
    /// Real JwtService wired with the shared test secret.
    /// Single source of truth for unit and integration suites.
    /// </summary>
    private static JwtService CreateJwtService(IDateTimeProvider timeProvider)
    {
        return new JwtService(
            TestJwtSecret,
            null,
            null,
            DefaultJwtOptions,
            Mock.Of<ILogger<JwtService>>(),
            timeProvider);
    }

    /// <summary>
    /// Builds a RefreshTokenService for the given context. Unit tests run on the
    /// InMemory provider, which cannot execute the PostgreSQL-only SQL
    /// (FOR UPDATE SKIP LOCKED, ExecuteUpdateAsync), so they get the testable
    /// subclass; integration tests run on real PostgreSQL and MUST exercise the
    /// production SQL paths (covered by AuthControllerServiceTests).
    /// </summary>
    private static RefreshTokenService CreateRefreshTokenService(AppDbContext db, IJwtService jwtService, IDateTimeProvider timeProvider)
        => db.Database.IsInMemory()
            ? new RefreshTokenServiceTests.TestableRefreshTokenService(db, jwtService, DefaultJwtOptions, timeProvider)
            : new RefreshTokenService(db, jwtService, DefaultJwtOptions, timeProvider);

    /// <summary>
    /// Builds an AccountLockoutService for the given context. Unit tests run on
    /// the InMemory provider, which cannot execute ExecuteUpdateAsync, so they
    /// get the testable subclass; integration tests run on real PostgreSQL and
    /// MUST exercise the production SQL paths (covered by AuthControllerServiceTests).
    /// </summary>
    private static AccountLockoutService CreateAccountLockoutService(AppDbContext db)
        => db.Database.IsInMemory()
            ? new AccountLockoutServiceTests.TestableAccountLockoutService(db, Mock.Of<ISecurityStampCache>())
            : new AccountLockoutService(db, Mock.Of<ISecurityStampCache>());

    /// <summary>
    /// AuthController over a real RefreshTokenService/UserAuthorizationService
    /// stack. No ControllerContext is attached; tests set their own HttpContext.
    /// </summary>
    public static AuthController CreateAuthController(AppDbContext db)
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var jwtService = CreateJwtService(timeProvider);
        var refreshService = CreateRefreshTokenService(db, jwtService, timeProvider);
        var accountLockout = CreateAccountLockoutService(db);
        return new AuthController(
            refreshService,
            accountLockout,
            jwtService,
            Options.Create(new AccountLockoutOptions()),
            Mock.Of<ILogger<AuthController>>(),
            timeProvider,
            new UserAuthorizationService(db, refreshService, accountLockout, Mock.Of<IFeatureCleanupService>(), timeProvider, Mock.Of<ISecurityStampCache>()),
            Mock.Of<ILocationQueryService>(),
            Mock.Of<IWebHostEnvironment>());
    }

    /// <summary>AdminSignupController over the same real service stack.</summary>
    public static AdminSignupController CreateAdminSignupController(AppDbContext db, IDbContextFactory<AppDbContext>? factory = null)
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        var jwtService = CreateJwtService(timeProvider);
        var refreshService = CreateRefreshTokenService(db, jwtService, timeProvider);
        var accountLockout = CreateAccountLockoutService(db);
        var authorizationService = new UserAuthorizationService(
            db, refreshService, accountLockout, Mock.Of<IFeatureCleanupService>(), timeProvider, Mock.Of<ISecurityStampCache>());
        return new AdminSignupController(
            accountLockout,
            Options.Create(new AccountLockoutOptions()),
            Options.Create(new AdminSignupOptions { SignupToken = TestData.AdminSignupToken }),
            Mock.Of<ILogger<AdminSignupController>>(),
            authorizationService,
            new UserCreationService(factory ?? new TestDbContextFactory(db), authorizationService, Mock.Of<ILogger<UserCreationService>>()),
            Mock.Of<IWebHostEnvironment>());
    }

    public static ClaimsPrincipal CreateClaimsPrincipal(
        Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null,
        string username = "testuser")
    {
        var claims = new List<Claim>
        {
            new(ClaimNames.UserId, userId.ToString()),
            new(ClaimNames.Username, username),
            new(ClaimNames.Role, role),
        };

        if (communeId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.CommuneId, communeId.Value.ToString()));
        }

        if (dairaId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.DairaId, dairaId.Value.ToString()));
        }

        if (wilayaId.HasValue)
        {
            claims.Add(new Claim(ClaimNames.WilayaId, wilayaId.Value.ToString()));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    public static void SetUser<T>(T controller, Guid userId, string role,
        int? communeId = null, int? dairaId = null, int? wilayaId = null, string? username = null)
        where T : ControllerBase => controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = CreateClaimsPrincipal(userId, role, communeId, dairaId, wilayaId, username ?? "testuser")
            }
        };

    public static void SetUser<T>(T controller, User user)
        where T : ControllerBase =>
        SetUser(controller, user.Id, user.Role, user.CommuneId, user.DairaId, user.WilayaId, user.Username);
}
