using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using Xunit;

namespace NarsApi.Tests;

public class AuthControllerTests
{
    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"NarsTest_{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    private static Mock<IConfiguration> CreateConfigMock(
        string secret = "test-secret-key-that-is-at-least-32-chars-long!!")
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Jwt:SecretKey"]).Returns(secret);
        config.Setup(c => c["Jwt:ExpiresInMinutes"]).Returns("60");
        config.Setup(c => c["Jwt:RefreshExpiresInDays"]).Returns("30");
        return config;
    }

    private static JwtService CreateJwtService(IConfiguration config, IDateTimeProvider? timeProvider = null)
    {
        timeProvider ??= Mock.Of<IDateTimeProvider>(x => x.UtcNow == DateTime.UtcNow);
        return new JwtService(
            "test-secret-key-that-is-at-least-32-chars-long!!",
            null,
            null,
            config,
            Mock.Of<ILogger<JwtService>>(),
            timeProvider);
    }

    private static AuthController CreateController(AppDbContext db, IConfiguration config)
    {
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == DateTime.UtcNow);
        return new AuthController(
            db,
            CreateJwtService(config, timeProvider),
            config,
            Mock.Of<ILogger<AuthController>>(),
            timeProvider);
    }

    [Fact]
    public void SignUp_PublicEndpointIsDisabled_Returns410()
    {
        var db = CreateInMemoryDbContext();
        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);

        var result = controller.SignUp();

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(410, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_ValidRequest_Returns201()
    {
        var db = CreateInMemoryDbContext();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: "Str0ng!Pass",
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "StrongP@ss1",
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_WeakPassword_Returns400()
    {
        var db = CreateInMemoryDbContext();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: "Str0ng!Pass",
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "weak",
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_DuplicateUsername_Returns409()
    {
        var db = CreateInMemoryDbContext();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            Email = "existing@example.com",
            Phone = "0555000000",
            Username = "existinguser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        await db.SaveChangesAsync();

        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: "Str0ng!Pass",
            Name: "New User",
            Email: "new@example.com",
            Phone: "0555123456",
            Username: "existinguser",
            Password: "StrongP@ss1",
            Role: UserRoles.CommuneUser,
            CommuneId: 1,
            DairaId: null,
            WilayaId: null
        ));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task AuthorizedAdminSignup_CommuneOutsideAdminScope_Returns403()
    {
        var db = CreateInMemoryDbContext();
        await SeedLocationDataAsync(db);
        await SeedAdminAsync(db, username: "admin", role: UserRoles.DairaAdmin, dairaId: 1);

        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);

        var result = await controller.AuthorizedAdminSignup(new AuthorizedAdminSignupRequest(
            AdminUsername: "admin",
            AdminPassword: "Str0ng!Pass",
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "StrongP@ss1",
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
        var db = CreateInMemoryDbContext();
        await SeedLocationDataAsync(db);
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = "Test User",
            Email = "test@example.com",
            Username = "testuser",
            Phone = "0555000000",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        await db.SaveChangesAsync();

        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        var result = await controller.SignIn(new SignInRequest(
            Username: "testuser",
            Password: "WrongP@ss1"
        ));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task SignIn_UserNotFound_Returns401()
    {
        var db = CreateInMemoryDbContext();
        var configMock = CreateConfigMock();
        var controller = CreateController(db, configMock.Object);

        var result = await controller.SignIn(new SignInRequest(
            Username: "nonexistent",
            Password: "Str0ng!Pass"
        ));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact(Skip = "EF Core InMemory provider does not support ExecuteUpdateAsync. " +
        "Logout test requires a real PostgreSQL database or a mock DbContext. " +
        "The controller logic (cookie deletion + success response) is verified " +
        "manually and in integration tests.")]
    public async Task Logout_WithAuthenticatedUser_ThrowsOnInMemoryExecuteUpdate()
    {
        // This test documents a known limitation: EF Core's InMemory provider
        // throws InvalidOperationException on ExecuteUpdateAsync calls.
        // See AuthController.Logout() → ExecuteUpdateAsync for revoking tokens.
        // When running against PostgreSQL this works correctly.
        await Task.CompletedTask;
    }

    private static async Task SeedLocationDataAsync(AppDbContext db)
    {
        db.Wilayas.Add(new Wilaya
        {
            WilayaId = 1,
            WilayaFr = "Alger",
            WilayaAr = "Alger",
            WilayaLatitude = 36.75,
            WilayaLongitude = 3.05,
        });
        db.Wilayas.Add(new Wilaya
        {
            WilayaId = 2,
            WilayaFr = "Blida",
            WilayaAr = "Blida",
            WilayaLatitude = 36.47,
            WilayaLongitude = 2.83,
        });
        db.Dairas.Add(new Daira
        {
            DairaId = 1,
            WilayaId = 1,
            DairaFr = "Draria",
            DairaAr = "Draria",
            DairaLatitude = 36.72,
            DairaLongitude = 2.96,
        });
        db.Dairas.Add(new Daira
        {
            DairaId = 2,
            WilayaId = 2,
            DairaFr = "Blida Centre",
            DairaAr = "Blida Centre",
            DairaLatitude = 36.47,
            DairaLongitude = 2.82,
        });
        db.Communes.Add(new Commune
        {
            CommuneId = 1,
            DairaId = 1,
            CommuneCode = 1001,
            CommuneFr = "Draria Centre",
            CommuneAr = "Draria Centre",
            CommuneLatitude = 36.72,
            CommuneLongitude = 2.96,
        });
        db.Communes.Add(new Commune
        {
            CommuneId = 2,
            DairaId = 2,
            CommuneCode = 2001,
            CommuneFr = "Blida Centre",
            CommuneAr = "Blida Centre",
            CommuneLatitude = 36.47,
            CommuneLongitude = 2.82,
        });
        await db.SaveChangesAsync();
    }

    private static async Task SeedAdminAsync(AppDbContext db, string username, string role, int? dairaId = null, int? wilayaId = null)
    {
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = "Admin User",
            Email = $"admin-{Guid.NewGuid():N}@test.com",
            Phone = "0555000000",
            Username = username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            Role = role,
            DairaId = dairaId,
            WilayaId = wilayaId,
        });
        await db.SaveChangesAsync();
    }
}
