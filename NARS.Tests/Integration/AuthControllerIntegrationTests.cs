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
using Xunit;

namespace NarsApi.Tests.Integration;

/// <summary>
/// Integration tests for AuthController against a real PostgreSQL database.
/// Tests the full stack: controller → EF Core → PostGIS.
/// </summary>
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

        var config = CreateConfigMock();
        var jwt = new JwtService("integration-test-secret-key-that-is-32chars!!", null, null, config.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<JwtService>>());
        _controller = new AuthController(_db, jwt, config.Object);
    }

    public async Task InitializeAsync()
    {
        // Seed reference data required for tests
        await SeedReferenceDataAsync();
    }

    public async Task DisposeAsync()
    {
        await _fixture.CleanTablesAsync();
    }

    [Fact]
    public async Task SignUp_ValidRequest_CreatesUser()
    {
        var result = await _controller.SignUp(new SignUpRequest(
            Name: "Integration Test User",
            Email: "integration@test.com",
            Phone: "0555999888",
            Username: "integration_user",
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

        var statusResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, statusResult.StatusCode);

        // Verify user was created in the database
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == "integration_user");
        Assert.NotNull(user);
        Assert.Equal("integration@test.com", user.Email);
        Assert.Equal(1, user.CommuneId);
    }

    [Fact]
    public async Task SignUp_DuplicateUsername_Returns409()
    {
        await _controller.SignUp(new SignUpRequest(
            Name: "User One",
            Email: "one@test.com",
            Phone: "0555111222",
            Username: "duplicate_user",
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

        var result = await _controller.SignUp(new SignUpRequest(
            Name: "User Two",
            Email: "two@test.com",
            Phone: "0555333444",
            Username: "duplicate_user",  // Same username
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task SignIn_CorrectCredentials_ReturnsTokens()
    {
        // Register a user first
        await _controller.SignUp(new SignUpRequest(
            Name: "Sign In User",
            Email: "signin@test.com",
            Phone: "0555123456",
            Username: "signin_user",
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

        var controller = CreateSignInController();
        var result = await controller.SignIn(new SignInRequest(
            Username: "signin_user",
            Password: "Str0ng!Pass"
        ));

        var okResult = Assert.IsType<OkObjectResult>(result);
        // The response is an anonymous type — just verify it's OK
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task SignIn_WrongPassword_Returns401()
    {
        await _controller.SignUp(new SignUpRequest(
            Name: "Wrong Pass User",
            Email: "wrong@test.com",
            Phone: "0555777888",
            Username: "wrong_pass",
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

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
        // Sign in to get tokens
        await _controller.SignUp(new SignUpRequest(
            Name: "Logout User",
            Email: "logout@test.com",
            Phone: "0555999000",
            Username: "logout_user",
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

        var signInController = CreateSignInController();
        await signInController.SignIn(new SignInRequest(
            Username: "logout_user",
            Password: "Str0ng!Pass"
        ));

        // Verify refresh token exists
        var userId = (await _db.Users.FirstAsync(u => u.Username == "logout_user")).Id;
        var tokenCount = await _db.RefreshTokens.CountAsync(rt => rt.UserId == userId && !rt.Revoked);
        Assert.True(tokenCount > 0);

        // Now logout
        var claims = new List<Claim> { new Claim("user_id", userId.ToString()) };
        var httpContext = CreateHttpContext(claims);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.Logout();

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Equal(200, okResult.StatusCode);

        // Verify tokens were revoked
        var revokedCount = await _db.RefreshTokens.CountAsync(rt => rt.UserId == userId && rt.Revoked);
        Assert.True(revokedCount > 0);
    }

    [Fact]
    public async Task CurrentUser_WithValidToken_ReturnsUserData()
    {
        await _controller.SignUp(new SignUpRequest(
            Name: "Current User",
            Email: "current@test.com",
            Phone: "0555111333",
            Username: "current_user",
            Password: "Str0ng!Pass",
            CommuneId: 1
        ));

        var user = await _db.Users.FirstAsync(u => u.Username == "current_user");
        var claims = new List<Claim>
        {
            new Claim("user_id", user.Id.ToString()),
            new Claim("commune_id", "1"),
        };
        var httpContext = CreateHttpContext(claims);
        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = await _controller.CurrentUser();

        var okResult = Assert.IsType<OkObjectResult>(result);
        // Verify response contains user data (anonymous type, can't cast directly)
        Assert.NotNull(okResult.Value);
    }

    private async Task SeedReferenceDataAsync()
    {
        // Only seed if tables are empty
        if (await _db.Communes.AnyAsync()) return;

        await _db.Wilayas.AddAsync(new Wilaya
        {
            WilayaId = 1,
            WilayaFr = "Alger",
            WilayaAr = "الجزائر",
            WilayaLatitude = 36.75,
            WilayaLongitude = 3.05,
        });

        await _db.Dairas.AddAsync(new Daira
        {
            DairaId = 1,
            WilayaId = 1,
            DairaFr = "Draria",
            DairaAr = "درارية",
            DairaLatitude = 36.72,
            DairaLongitude = 2.96,
        });

        await _db.Communes.AddAsync(new Commune
        {
            CommuneId = 1,
            DairaId = 1,
            CommuneCode = 1001,
            CommuneFr = "Draria Centre",
            CommuneAr = "درارية الوسطى",
            CommuneLatitude = 36.72,
            CommuneLongitude = 2.96,
        });

        // Seed a simple commune boundary polygon (a small square near Draria)
        var factory = NetTopologySuite.NtsGeometryServices.Instance.CreateGeometryFactory(4326);
        var boundary = factory.CreatePolygon(new[]
        {
            new NetTopologySuite.Geometries.Coordinate(2.95, 36.71),
            new NetTopologySuite.Geometries.Coordinate(2.97, 36.71),
            new NetTopologySuite.Geometries.Coordinate(2.97, 36.73),
            new NetTopologySuite.Geometries.Coordinate(2.95, 36.73),
            new NetTopologySuite.Geometries.Coordinate(2.95, 36.71),
        });

        await _db.CommuneBoundaries.AddAsync(new CommuneBoundary
        {
            CommuneId = 1,
            Geometry = boundary,
        });

        await _db.SaveChangesAsync();
    }

    private static Mock<IConfiguration> CreateConfigMock()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["Jwt:SecretKey"]).Returns("integration-test-secret-key-that-is-32chars!!");
        config.Setup(c => c["Jwt:ExpiresInMinutes"]).Returns("60");
        config.Setup(c => c["Jwt:RefreshExpiresInDays"]).Returns("30");
        return config;
    }

    private static DefaultHttpContext CreateHttpContext(List<Claim> claims)
    {
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal(identity);
        return httpContext;
    }

    private AuthController CreateSignInController()
    {
        var config = CreateConfigMock();
        var jwt = new JwtService("integration-test-secret-key-that-is-32chars!!", null, null, config.Object, Mock.Of<Microsoft.Extensions.Logging.ILogger<JwtService>>());
        var ctrl = new AuthController(_db, jwt, config.Object);
        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext(),
        };
        return ctrl;
    }
}
