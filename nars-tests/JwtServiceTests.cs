using Xunit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NarsApi.Services;

namespace NarsApi.Tests;

public class JwtServiceTests
{
    private static JwtService CreateService(
        string secret = "this-is-a-test-secret-key-that-is-long-enough-32chars!",
        int expiresMinutes = 60,
        int refreshExpiresDays = 30)
    {
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["Jwt:SecretKey"]).Returns(secret);
        configMock.Setup(c => c["Jwt:ExpiresInMinutes"]).Returns(expiresMinutes.ToString());
        configMock.Setup(c => c["Jwt:RefreshExpiresInDays"]).Returns(refreshExpiresDays.ToString());

        var loggerMock = Mock.Of<ILogger<JwtService>>();
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == DateTime.UtcNow);
        return new JwtService(secret, null, null, configMock.Object, loggerMock, timeProvider);
    }

    [Fact]
    public void CreateToken_ReturnsValidJwt()
    {
        var service = CreateService();
        var token = service.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", "test@example.com", 1);

        Assert.NotNull(token);
        Assert.NotEmpty(token);
        // JWT has 3 parts separated by dots
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void CreateToken_ContainsCorrectClaims()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var token = service.CreateToken(
            userId, "testuser", "Test User", "test@example.com", 42);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal(userId.ToString(),
            jwt.Claims.First(c => c.Type == "user_id").Value);
        Assert.Equal("testuser",
            jwt.Claims.First(c => c.Type == "username").Value);
        Assert.Equal("Test User",
            jwt.Claims.First(c => c.Type == "name").Value);
        Assert.Equal("test@example.com",
            jwt.Claims.First(c => c.Type == "email").Value);
        Assert.Equal("42",
            jwt.Claims.First(c => c.Type == "commune_id").Value);
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipal()
    {
        var service = CreateService();
        var token = service.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", "test@example.com", 1);

        var principal = service.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal("testuser", principal.FindFirst("username")?.Value);
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var service = CreateService();
        var token = service.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", "test@example.com", 1);

        // Tamper with the token
        var tampered = token.Substring(0, token.Length - 5) + "XXXXX";

        Assert.Null(service.ValidateToken(tampered));
    }

    [Fact]
    public void ValidateToken_EmptyToken_ReturnsNull()
    {
        var service = CreateService();
        Assert.Null(service.ValidateToken(""));
    }

    [Fact]
    public void ValidateToken_WrongSecret_ReturnsNull()
    {
        var issuer = CreateService(secret: "correct-secret-key-that-is-long-enough!!");
        var token = issuer.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", "test@example.com", 1);

        var verifier = CreateService(secret: "wrong-secret-key-that-is-long-enough-32chars!!");

        Assert.Null(verifier.ValidateToken(token));
    }

    [Fact]
    public void CreateRefreshToken_ReturnsRawAndHash()
    {
        var service = CreateService();
        var (raw, hash) = service.CreateRefreshToken();

        Assert.NotNull(raw);
        Assert.NotEmpty(raw);
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        // Hash should be different from raw
        Assert.NotEqual(raw, hash);
        // Hash should be a valid Base64 string
        Assert.NotNull(Convert.FromBase64String(hash));
    }

    [Fact]
    public void CreateRefreshToken_UniqueTokens()
    {
        var service = CreateService();
        var (raw1, hash1) = service.CreateRefreshToken();
        var (raw2, hash2) = service.CreateRefreshToken();

        Assert.NotEqual(raw1, raw2);
        Assert.NotEqual(hash1, hash2);
    }
}
