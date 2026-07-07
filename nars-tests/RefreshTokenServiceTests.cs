using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
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
    private static readonly DateTime FixedNow = new(2025, 6, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid UserId = Guid.NewGuid();

    private static AppDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"RefreshTokenTest_{Guid.NewGuid()}")
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new AppDbContext(options);
    }

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
        Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedNow);

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
                .FirstOrDefaultAsync(rt => rt.TokenHash == hash && !rt.Revoked && rt.ExpiresAt > FixedNow, ct);
    }

    private static TestableRefreshTokenService CreateService(AppDbContext db, IJwtService? jwt = null, IDateTimeProvider? timeProvider = null)
    {
        var jwtOptions = Options.Create(new JwtOptions { ExpiresInMinutes = 60, RefreshExpiresInDays = 30 });
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
            Email = "test@test.com",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = "commune_user",
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
            ExpiresAt = FixedNow.AddDays(30),
            CreatedAt = FixedNow,
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
        var (svc, db, raw) = await SeedWithValidTokenAsync();

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

    [Fact]
    public async Task RotateRefreshTokenAsync_ExpiredToken_ReturnsFailure()
    {
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = "test@test.com",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = "commune_user",
            CommuneId = 1,
        });
        const string raw = "expired-token";
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TokenHash = hash,
            ExpiresAt = FixedNow.AddDays(-1),
            CreatedAt = FixedNow,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.False(result.Success);
        Assert.Equal("Invalid or expired refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_RevokedToken_ReturnsFailure()
    {
        var db = CreateDb();
        db.Users.Add(new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = "test@test.com",
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = "commune_user",
            CommuneId = 1,
        });
        const string raw = "revoked-token";
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TokenHash = hash,
            ExpiresAt = FixedNow.AddDays(30),
            CreatedAt = FixedNow,
            Revoked = true,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.False(result.Success);
        Assert.Equal("Invalid or expired refresh token.", result.Detail);
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_DeletedUser_ReturnsFailure()
    {
        var db = CreateDb();
        const string raw = "orphan-token";
        var hash = Convert.ToBase64String(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(raw)));
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            TokenHash = hash,
            ExpiresAt = FixedNow.AddDays(30),
            CreatedAt = FixedNow,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var result = await svc.RotateRefreshTokenAsync(raw);

        Assert.False(result.Success);
        Assert.Equal("User no longer exists.", result.Detail);
    }
}
