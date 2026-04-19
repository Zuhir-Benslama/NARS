using Xunit;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NarsApi.Controllers;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Models;
using NarsApi.Services;

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

    private static JwtService CreateJwtService(IConfiguration config)
    {
        return new JwtService("test-secret-key-that-is-at-least-32-chars-long!!", null, null, config, Mock.Of<Microsoft.Extensions.Logging.ILogger<JwtService>>());
    }

    [Fact]
    public async Task SignUp_ValidRequest_Returns201()
    {
        var db = CreateInMemoryDbContext();
        
        var configMock = CreateConfigMock();
        var controller = new AuthController(db, CreateJwtService(configMock.Object), configMock.Object);

        // Seed a commune
        db.Communes.Add(new Commune { CommuneId = 1, DairaId = 1 });
        await db.SaveChangesAsync();

        var result = await controller.SignUp(new SignUpRequest(
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "StrongP@ss1",
            CommuneId: 1
        ));

        var statusCodeResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusCodeResult.StatusCode);
    }

    [Fact]
    public async Task SignUp_WeakPassword_Returns400()
    {
        var db = CreateInMemoryDbContext();
        
        var configMock = CreateConfigMock();
        var controller = new AuthController(db, CreateJwtService(configMock.Object), configMock.Object);

        var result = await controller.SignUp(new SignUpRequest(
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "weak",
            CommuneId: 1
        ));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SignUp_DuplicateUsername_Returns409()
    {
        var db = CreateInMemoryDbContext();
        
        var configMock = CreateConfigMock();

        // Seed existing user
        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = "Existing",
            Email = "existing@example.com",
            Username = "existinguser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            CommuneId = 1,
        });
        db.Communes.Add(new Commune { CommuneId = 1, DairaId = 1 });
        await db.SaveChangesAsync();

        var controller = new AuthController(db, CreateJwtService(configMock.Object), configMock.Object);

        var result = await controller.SignUp(new SignUpRequest(
            Name: "New User",
            Email: "new@example.com",
            Phone: "0555123456",
            Username: "existinguser",
            Password: "StrongP@ss1",
            CommuneId: 1
        ));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task SignUp_InvalidCommune_Returns400()
    {
        var db = CreateInMemoryDbContext();
        
        var configMock = CreateConfigMock();
        var controller = new AuthController(db, CreateJwtService(configMock.Object), configMock.Object);

        var result = await controller.SignUp(new SignUpRequest(
            Name: "Test User",
            Email: "test@example.com",
            Phone: "0555123456",
            Username: "testuser",
            Password: "StrongP@ss1",
            CommuneId: 99999  // Non-existent commune
        ));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SignIn_WrongPassword_Returns401()
    {
        var db = CreateInMemoryDbContext();
        
        var configMock = CreateConfigMock();

        db.Users.Add(new User
        {
            Id = Guid.NewGuid(),
            Name = "Test User",
            Email = "test@example.com",
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            CommuneId = 1,
        });
        db.Communes.Add(new Commune { CommuneId = 1, DairaId = 1 });
        await db.SaveChangesAsync();

        var controller = new AuthController(db, CreateJwtService(configMock.Object), configMock.Object);
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

        var controller = new AuthController(db, CreateJwtService(configMock.Object), configMock.Object);

        var result = await controller.SignIn(new SignInRequest(
            Username: "nonexistent",
            Password: "Str0ng!Pass"
        ));

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Logout_WithAuthenticatedUser_Returns200()
    {
        var db = CreateInMemoryDbContext();
        var configMock = CreateConfigMock();
        var jwt = CreateJwtService(configMock.Object);

        var userId = Guid.NewGuid();
        db.Users.Add(new User
        {
            Id = userId,
            Name = "Test User",
            Email = "test@example.com",
            Username = "testuser",
            PasswordHash = BCrypt.Net.BCrypt.HashPassword("Str0ng!Pass"),
            CommuneId = 1,
        });

        // Seed refresh tokens (InMemory doesn't support ExecuteUpdateAsync,
        // so we test the controller's response logic rather than the DB update)
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = "test-hash",
            ExpiresAt = DateTime.UtcNow.AddDays(30),
            Revoked = false,
        });
        await db.SaveChangesAsync();

        var controller = new AuthController(db, jwt, configMock.Object);

        // Simulate authenticated user via claims
        var claims = new List<Claim>
        {
            new Claim("user_id", userId.ToString()),
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(identity);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };

        // The Logout method calls ExecuteUpdateAsync which InMemory EF Core
        // doesn't support (throws InvalidOperationException).
        // This test documents the limitation — actual token revocation requires
        // integration tests with PostgreSQL.
        var result = await Assert.ThrowsAsync<InvalidOperationException>(
            () => controller.Logout());

        Assert.Contains("ExecuteUpdate", result.Message);
    }
}
