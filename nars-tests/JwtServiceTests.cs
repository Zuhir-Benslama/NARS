using Xunit;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Infrastructure;
using NarsApi.Services;
using static NarsApi.Tests.TestData;

namespace NarsApi.Tests;

public class JwtServiceTests
{
    private static JwtService CreateService(
        string secret = AuthTestHelper.TestJwtSecret,
        int expiresMinutes = 60,
        int refreshExpiresDays = 30)
    {
        var jwtOptions = Options.Create(new JwtOptions
        {
            ExpiresInMinutes = expiresMinutes,
            RefreshExpiresInDays = refreshExpiresDays,
        });

        var loggerMock = Mock.Of<ILogger<JwtService>>();
        var timeProvider = Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);
        return new JwtService(secret, null, null, jwtOptions, loggerMock, timeProvider);
    }

    [Fact]
    public void CreateToken_ReturnsValidJwt()
    {
        var service = CreateService();
        var token = service.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", DefaultEmail, 1);

        Assert.NotNull(token);
        Assert.NotEmpty(token);
        Assert.Equal(3, token.Split('.').Length);
    }

    [Fact]
    public void CreateToken_ContainsCorrectClaims()
    {
        var service = CreateService();
        var userId = Guid.NewGuid();
        var token = service.CreateToken(
            userId, "testuser", "Test User", DefaultEmail, 42);

        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);

        Assert.Equal(userId.ToString(),
            jwt.Claims.First(c => c.Type == ClaimNames.UserId).Value);
        Assert.Equal("testuser",
            jwt.Claims.First(c => c.Type == ClaimNames.Username).Value);
        Assert.Equal("Test User",
            jwt.Claims.First(c => c.Type == ClaimNames.Name).Value);
        Assert.Equal(DefaultEmail,
            jwt.Claims.First(c => c.Type == ClaimNames.Email).Value);
        Assert.Equal("42",
            jwt.Claims.First(c => c.Type == ClaimNames.CommuneId).Value);
    }

    [Fact]
    public void ValidateToken_ValidToken_ReturnsPrincipal()
    {
        var service = CreateService();
        var token = service.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", DefaultEmail, 1);

        var principal = service.ValidateToken(token);

        Assert.NotNull(principal);
        Assert.Equal("testuser", principal.FindFirst(ClaimNames.Username)?.Value);
    }

    [Fact]
    public void ValidateToken_TamperedToken_ReturnsNull()
    {
        var service = CreateService();
        var token = service.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", DefaultEmail, 1);

        // Tamper with the token
        var tampered = token[..^5] + "XXXXX";

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
            Guid.NewGuid(), "testuser", "Test User", DefaultEmail, 1);

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
        // Hash should be a valid Base64 string (decodes without throwing)
        _ = Convert.FromBase64String(hash);
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

    [Fact]
    public void ValidateToken_ExpiredToken_ReturnsNull()
    {
        var expiredService = CreateService(expiresMinutes: -1);
        var token = expiredService.CreateToken(
            Guid.NewGuid(), "testuser", "Test User", DefaultEmail, 1);

        var principal = expiredService.ValidateToken(token);

        Assert.Null(principal);
    }
}
