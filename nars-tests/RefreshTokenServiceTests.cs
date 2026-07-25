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
    ///
    /// This means unit tests exercise different code paths than production.
    /// Integration tests (AuthControllerIntegrationTests, FeatureStatsServiceTests, etc.)
    /// cover the real PostgreSQL code paths via Testcontainers.
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
                .FirstOrDefaultAsync(rt => rt.TokenHash == hash && !rt.Revoked && rt.ExpiresAt > TimeProvider.UtcNow, ct);

        public override async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var tokens = await _db.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.Revoked)
                .ToListAsync(cancellationToken);
            foreach (var t in tokens)
                t.Revoked = true;
            await _db.SaveChangesAsync(cancellationToken);
        }
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

    // ── RevokeAllUserTokensAsync ────────────────────────────────────────

    [Fact]
    public async Task RevokeAllUserTokensAsync_RevokesUnrevokedTokens()
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
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TokenHash = "tok1",
            ExpiresAt = FixedUtcNow.AddDays(30),
            CreatedAt = FixedUtcNow,
        });
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TokenHash = "tok2",
            ExpiresAt = FixedUtcNow.AddDays(60),
            CreatedAt = FixedUtcNow,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.RevokeAllUserTokensAsync(UserId);

        var tokens = await db.RefreshTokens.Where(t => t.UserId == UserId).ToListAsync();
        Assert.All(tokens, t => Assert.True(t.Revoked));
    }

    [Fact]
    public async Task RevokeAllUserTokensAsync_AlreadyRevokedTokens_AreNotAffected()
    {
        var db = CreateDb();
        var otherUserId = Guid.NewGuid();
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
        db.RefreshTokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            TokenHash = "revoked-tok",
            ExpiresAt = FixedUtcNow.AddDays(30),
            CreatedAt = FixedUtcNow,
            Revoked = true,
        });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.RevokeAllUserTokensAsync(UserId);

        var token = await db.RefreshTokens.FirstAsync(t => t.UserId == UserId);
        Assert.True(token.Revoked);
    }

    // ── IssueRefreshTokenAsync ──────────────────────────────────────────

    [Fact]
    public async Task IssueRefreshTokenAsync_ReturnsRawHashAndExpiry()
    {
        var db = CreateDb();
        var svc = CreateService(db);

        var (raw, hash, expiresAt) = await svc.IssueRefreshTokenAsync(UserId);

        Assert.False(string.IsNullOrEmpty(raw));
        Assert.False(string.IsNullOrEmpty(hash));
        Assert.Equal(FixedUtcNow.AddDays(30), expiresAt);
        var stored = await db.RefreshTokens.SingleAsync(t => t.TokenHash == hash);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal(expiresAt, stored.ExpiresAt);
    }

    [Fact]
    public async Task IssueRefreshTokenAsync_DifferentCallsProduceDifferentTokens()
    {
        var db = CreateDb();
        var callCount = 0;
        var jwtMock = new Mock<IJwtService>();
        jwtMock.Setup(j => j.CreateRefreshToken()).Returns(() =>
        {
            callCount++;
            return ($"raw-{callCount}", $"hash-{callCount}");
        });
        jwtMock.Setup(j => j.AccessTokenExpiresIn).Returns(TimeSpan.FromMinutes(60));
        var svc = CreateService(db, jwtMock.Object);

        var (raw1, hash1, _) = await svc.IssueRefreshTokenAsync(UserId);
        var (raw2, hash2, _) = await svc.IssueRefreshTokenAsync(UserId);

        Assert.NotEqual(raw1, raw2);
        Assert.NotEqual(hash1, hash2);
        Assert.Equal(2, await db.RefreshTokens.CountAsync());
    }

    // ── FindUserByIdAsync ───────────────────────────────────────────────

    [Fact]
    public async Task FindUserByIdAsync_ExistingUser_ReturnsUser()
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
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var found = await svc.FindUserByIdAsync(UserId);

        Assert.NotNull(found);
        Assert.Equal(UserId, found.Id);
        Assert.Equal("testuser", found.Username);
    }

    [Fact]
    public async Task FindUserByIdAsync_NonexistentUser_ReturnsNull()
    {
        var db = CreateDb();
        var svc = CreateService(db);

        var found = await svc.FindUserByIdAsync(Guid.NewGuid());

        Assert.Null(found);
    }

    // ── FindUserByUsernameAsync ──────────────────────────────────────────

    [Fact]
    public async Task FindUserByUsernameAsync_ExistingUser_ReturnsUser()
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
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        var found = await svc.FindUserByUsernameAsync("testuser");

        Assert.NotNull(found);
        Assert.Equal(UserId, found.Id);
    }

    [Fact]
    public async Task FindUserByUsernameAsync_NonexistentUser_ReturnsNull()
    {
        var db = CreateDb();
        var svc = CreateService(db);

        var found = await svc.FindUserByUsernameAsync("no-such-user");

        Assert.Null(found);
    }

    // ── SaveUserAsync (via UserCreationService) ──────────────────────────

    [Fact]
    public async Task SaveUserAsync_PersistsNewUser()
    {
        var db = CreateDb();
        var user = new User
        {
            Id = UserId,
            Username = "newuser",
            Name = "New User",
            Email = DefaultEmail,
            Phone = AltPhone,
            PasswordHash = "pw-hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 2,
        };

        var svc = new UserCreationService(db);
        await svc.SaveUserAsync(user);

        var stored = await db.Users.FindAsync(UserId);
        Assert.NotNull(stored);
        Assert.Equal("newuser", stored.Username);
        Assert.Equal(DefaultEmail, stored.Email);
    }

    // ── RecordFailedLoginAsync ──────────────────────────────────────────

    [Fact]
    public async Task RecordFailedLoginAsync_IncrementsAttempts()
    {
        var db = CreateDb();
        var user = new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
            FailedLoginAttempts = 0,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task RecordFailedLoginAsync_AtThreshold_LocksUser()
    {
        var db = CreateDb();
        var user = new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
            FailedLoginAttempts = 4,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        Assert.Equal(5, user.FailedLoginAttempts);
        Assert.Equal(FixedUtcNowOffset.DateTime.AddMinutes(15), user.LockedUntil);
    }

    // ── ResetFailedAttemptsIfNeededAsync ────────────────────────────────

    [Fact]
    public async Task ResetFailedAttemptsIfNeededAsync_ClearsState()
    {
        var db = CreateDb();
        var user = new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
            FailedLoginAttempts = 3,
            LockedUntil = FixedUtcNow.AddMinutes(30),
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.ResetFailedAttemptsIfNeededAsync(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task ResetFailedAttemptsIfNeededAsync_AlreadyClean_NoSave()
    {
        var db = CreateDb();
        var user = new User
        {
            Id = UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
            FailedLoginAttempts = 0,
            LockedUntil = null,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var svc = CreateService(db);

        await svc.ResetFailedAttemptsIfNeededAsync(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }
}
