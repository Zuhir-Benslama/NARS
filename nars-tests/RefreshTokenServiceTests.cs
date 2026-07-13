using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using NarsApi.Data;
using NarsApi.DTOs;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class RefreshTokenServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static AppDbContext CreateDb() => CreateInMemoryDb("RefreshTokenTest");

    private static Mock<IJwtService> CreateJwtMock()
    {
        var mock = new Mock<IJwtService>();
        mock.Setup(j => j.CreateRefreshToken()).Returns(("raw-token", "hashed-token"));
        mock.Setup(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns("new-access-token");
        mock.Setup(j => j.AccessTokenExpiresIn).Returns(TimeSpan.FromMinutes(60));
        return mock;
    }

    private static IDateTimeProvider CreateTimeProvider() =>
        Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);

    /// <summary>
    /// A testable subclass that replaces the PostgreSQL-specific FOR UPDATE SKIP LOCKED
    /// query with a standard LINQ query usable with the InMemory provider.
    /// </summary>
    private sealed class TestableRefreshTokenService : RefreshTokenService
    {
        private readonly AppDbContext _db;

        public TestableRefreshTokenService(
            AppDbContext db,
            IJwtService jwt,
            IOptions<JwtOptions> jwtOptions,
            IDateTimeProvider timeProvider)
            : base(db, jwt, jwtOptions, timeProvider)
        {
            _db = db;
        }

        protected override Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken ct)
            => _db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == hash && !rt.Revoked && rt.ExpiresAt > FixedUtcNow, ct);
    }

    private static TestableRefreshTokenService CreateService(AppDbContext db, IJwtService? jwt = null, IDateTimeProvider? timeProvider = null)
    {
        var jwtOptions = DefaultJwtOptions;
        return new TestableRefreshTokenService(
            db,
            jwt ?? CreateJwtMock().Object,
            jwtOptions,
            timeProvider ?? CreateTimeProvider());
    }

    private static async Task<(TestableRefreshTokenService Service, AppDbContext Db, string RawToken)> SeedWithValidTokenAsync()
    {
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
        });
        const string raw = "valid-refresh-token";
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TokenHash = hash,
            ExpiresAt = FixedUtcNow.AddDays(30),
            CreatedAt = FixedUtcNow,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);
        return (svc, db, raw);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_NullToken_ReturnsFailure()
    {
        var (svc, _, _) = await SeedWithValidTokenAsync();

        var result = await svc.RotateRefreshTokenAsync(null);

        Assert.False(result.Success);
        Assert.Equal("No refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_EmptyToken_ReturnsFailure()
    {
        var (svc, _, _) = await SeedWithValidTokenAsync();

        var result = await svc.RotateRefreshTokenAsync("");

        Assert.False(result.Success);
        Assert.Equal("No refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_InvalidToken_ReturnsFailure()
    {
        var (svc, _, _) = await SeedWithValidTokenAsync();

        var result = await svc.RotateRefreshTokenAsync("invalid-token");

        Assert.False(result.Success);
        Assert.Equal("Invalid or expired refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ValidToken_ReturnsSuccessWithNewToken()
    {
        var (svc, _, raw) = await SeedWithValidTokenAsync();

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.True(result.Success);
        Assert.NotNull(result.NewAccessToken);
        Assert.NotNull(result.NewRawToken);
        Assert.Equal("testuser", result.Username);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_RevokesOldToken()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();

        await svc.RotateRefreshTokenAsync(raw);

        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        var oldToken = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hash);
        Assert.True(oldToken.Revoked);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_IssuesNewRefreshToken()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();

        await svc.RotateRefreshTokenAsync(raw);

        var unrevoked = await db.RefreshTokens.CountAsync(rt => !rt.Revoked);
        Assert.Equal(1, unrevoked);
    }

    private static async Task<(AppDbContext Db, string RawToken)> SeedTokenAsync(
        DateTime expiresAt, bool revoked = false, Guid? userId = null, bool skipUser = false)
    {
        var db = CreateDb();
        var uid = userId ?? UserId;
        if (!skipUser)
        {
            db.Users.Add(new User
            {
                Id = uid,
                Username = "testuser",
                Name = "Test User",
                Email = AltEmail,
                Phone = DefaultPhone,
                PasswordHash = "hash",
                Role = UserRoles.CommuneUser,
                CommuneId = 1,
            });
        }
        var raw = $"{(revoked ? "revoked" : "expired")}-token-{Guid.NewGuid():N}";
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = uid,
            TokenHash = hash,
            ExpiresAt = expiresAt,
            CreatedAt = FixedUtcNow,
            Revoked = revoked,
        });
        await db.SaveChangesAsync();
        return (db, raw);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ExpiredToken_ReturnsFailure()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(-1));
        var svc = CreateService(db);

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.False(result.Success);
        Assert.Equal("Invalid or expired refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_RevokedToken_ReturnsFailure()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30), revoked: true);
        var svc = CreateService(db);

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.False(result.Success);
        Assert.Equal("Invalid or expired refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_DeletedUser_ReturnsFailure()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30), userId: Guid.NewGuid(), skipUser: true);
        var svc = CreateService(db);

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.False(result.Success);
        Assert.Equal("User no longer exists.", result.Detail);
    }
}
