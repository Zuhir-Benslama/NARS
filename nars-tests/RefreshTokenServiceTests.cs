using Microsoft.EntityFrameworkCore;
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
    private const string OriginalStamp = "original-stamp";

    private static AppDbContext CreateDb() => CreateInMemoryDb("RefreshTokenTest");

    private static string HashRefreshToken(string raw) =>
        Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw)));

    private static Mock<IJwtService> CreateJwtMock()
    {
        var mock = new Mock<IJwtService>();
        mock.Setup(j => j.CreateRefreshToken()).Returns(("raw-token", "hashed-token"));
        mock.Setup(j => j.CreateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<string>(),
            It.IsAny<string>(), It.IsAny<int?>(), It.IsAny<int?>()))
            .Returns("new-access-token");
        mock.Setup(j => j.AccessTokenExpiresIn).Returns(TimeSpan.FromMinutes(60));
        return mock;
    }

    private static IDateTimeProvider CreateTimeProvider() =>
        Mock.Of<IDateTimeProvider>(x => x.UtcNow == FixedUtcNow);

    /// <summary>
    /// A testable subclass that replaces PostgreSQL-specific queries (FOR UPDATE SKIP LOCKED,
    /// ExecuteUpdateAsync) with standard LINQ equivalents usable with the InMemory provider.
    ///
    /// This means unit tests exercise slightly different code paths than production.
    /// Integration tests (AuthControllerServiceTests, FeatureStatsServiceTests, etc.)
    /// cover the real PostgreSQL code paths via Testcontainers.
    /// </summary>
    private sealed class TestableRefreshTokenService(
        AppDbContext db,
        IJwtService jwt,
        IOptions<JwtOptions> jwtOptions,
        ISecurityStampCache stampCache,
        IDateTimeProvider timeProvider) : RefreshTokenService(db, jwt, jwtOptions, stampCache, timeProvider)
    {
        private readonly AppDbContext _db = db;

        protected override Task<RefreshToken?> FindRefreshTokenByHashAsync(string hash, CancellationToken ct)
            => _db.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.TokenHash == hash && rt.ExpiresAt > TimeProvider.UtcNow, ct);

        public override async Task RevokeAllUserTokensAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var tokens = await _db.RefreshTokens
                .Where(rt => rt.UserId == userId && !rt.Revoked)
                .ToListAsync(cancellationToken);
            foreach (var t in tokens)
            {
                t.Revoked = true;
            }

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
            Mock.Of<ISecurityStampCache>(),
            timeProvider ?? CreateTimeProvider());
    }

    private static async Task<User> SeedUserAsync(
        AppDbContext db,
        Guid? userId = null,
        string? securityStamp = null,
        int failedLoginAttempts = 0,
        DateTime? lockedUntil = null)
    {
        var user = new User
        {
            Id = userId ?? UserId,
            Username = "testuser",
            Name = "Test User",
            Email = AltEmail,
            Phone = DefaultPhone,
            PasswordHash = "hash",
            Role = UserRoles.CommuneUser,
            CommuneId = 1,
            SecurityStamp = securityStamp ?? User.GenerateSecurityStamp(),
            FailedLoginAttempts = failedLoginAttempts,
            LockedUntil = lockedUntil,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static async Task<(TestableRefreshTokenService Service, AppDbContext Db, string RawToken)> SeedWithValidTokenAsync()
    {
        var db = CreateDb();
        await SeedUserAsync(db);
        const string raw = "valid-refresh-token";
        var hash = HashRefreshToken(raw);
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
        var (svc, db, _) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.RotateRefreshTokenAsync(null);

            Assert.False(result.Success);
            Assert.Equal("No refresh token.", result.Detail);
        }
    }

    // ── MintAccessTokenAsync ──────────────────────────────────────────────

    [Fact]
    public async Task MintAccessTokenAsync_ValidToken_DoesNotRevokeAndReturnsAccessToken()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.MintAccessTokenAsync(raw);

            Assert.True(result.Success);
            Assert.Equal("new-access-token", result.NewAccessToken);
            Assert.Null(result.NewRawToken);

            var stored = await db.RefreshTokens.SingleAsync();
            Assert.False(stored.Revoked);
        }
    }

    [Fact]
    public async Task MintAccessTokenAsync_NullToken_ReturnsFailure()
    {
        var (svc, db, _) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.MintAccessTokenAsync(null);

            Assert.False(result.Success);
            Assert.Equal("No refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task MintAccessTokenAsync_UnknownToken_ReturnsFailure()
    {
        var (svc, db, _) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.MintAccessTokenAsync("bogus-token");

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task MintAccessTokenAsync_RevokedToken_ReturnsFailure()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();
        using (db)
        {
            await svc.RotateRefreshTokenAsync(raw);

            var result = await svc.MintAccessTokenAsync(raw);

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task MintAccessTokenAsync_ConcurrentPageLoads_AllSucceed()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();
        using (db)
        {
            var results = await Task.WhenAll(
                Enumerable.Range(0, 5).Select(_ => svc.MintAccessTokenAsync(raw)));

            Assert.All(results, r =>
            {
                Assert.True(r.Success);
                Assert.Equal("new-access-token", r.NewAccessToken);
            });

            var stored = await db.RefreshTokens.SingleAsync();
            Assert.False(stored.Revoked);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_EmptyToken_ReturnsFailure()
    {
        var (svc, db, _) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.RotateRefreshTokenAsync("");

            Assert.False(result.Success);
            Assert.Equal("No refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_InvalidToken_ReturnsFailure()
    {
        var (svc, db, _) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.RotateRefreshTokenAsync("invalid-token");

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ValidToken_ReturnsSuccessWithNewToken()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();
        using (db)
        {
            var result = await svc.RotateRefreshTokenAsync(raw);

            Assert.True(result.Success);
            Assert.Equal("testuser", result.Username);
            Assert.Equal("new-access-token", result.NewAccessToken);
            Assert.Equal("raw-token", result.NewRawToken);
            Assert.Equal(FixedUtcNow.AddDays(30), result.RefreshExpiry);

            // Round-trip: the freshly issued raw token's hash is persisted for this user.
            var newToken = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == "hashed-token");
            Assert.Equal(UserId, newToken.UserId);
            Assert.False(newToken.Revoked);
            Assert.Equal(FixedUtcNow.AddDays(30), newToken.ExpiresAt);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_RevokesOldToken()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();
        using (db)
        {
            await svc.RotateRefreshTokenAsync(raw);

            var hash = HashRefreshToken(raw);
            var oldToken = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hash);
            Assert.True(oldToken.Revoked);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_IssuesNewRefreshToken()
    {
        var (svc, db, raw) = await SeedWithValidTokenAsync();
        using (db)
        {
            await svc.RotateRefreshTokenAsync(raw);

            var unrevoked = await db.RefreshTokens.CountAsync(rt => !rt.Revoked);
            Assert.Equal(1, unrevoked);
        }
    }

    private static async Task<(AppDbContext Db, string RawToken)> SeedTokenAsync(
        DateTime expiresAt, bool revoked = false, Guid? userId = null, bool skipUser = false)
    {
        var db = CreateDb();
        var uid = userId ?? UserId;
        if (!skipUser)
        {
            await SeedUserAsync(db, uid);
        }
        var raw = $"{(revoked ? "revoked" : "expired")}-token-{Guid.NewGuid():N}";
        var hash = HashRefreshToken(raw);
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
        using (db)
        {
            var svc = CreateService(db);

            var result = await svc.RotateRefreshTokenAsync(raw);

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_RevokedToken_ReturnsFailure()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30), revoked: true);
        using (db)
        {
            var svc = CreateService(db);

            var result = await svc.RotateRefreshTokenAsync(raw);

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired refresh token.", result.Detail);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ReplayOfRevokedToken_RevokesAllTokensForUser()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30));
        using (db)
        {
            var svc = CreateService(db);

            // First rotation succeeds and revokes the old token...
            var first = await svc.RotateRefreshTokenAsync(raw);
            Assert.True(first.Success);

            // ...the fresh token keeps the session alive.
            var active = await db.RefreshTokens.CountAsync(rt => !rt.Revoked);
            Assert.Equal(1, active);

            // Replaying the old (now-revoked) token signals theft: every
            // outstanding token for the user must be revoked.
            var replay = await svc.RotateRefreshTokenAsync(raw);

            Assert.False(replay.Success);
            Assert.Equal("Invalid or expired refresh token.", replay.Detail);
            Assert.Equal(0, await db.RefreshTokens.CountAsync(rt => !rt.Revoked));
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_DeletedUser_ReturnsFailure()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30), userId: Guid.NewGuid(), skipUser: true);
        using (db)
        {
            var svc = CreateService(db);

            var result = await svc.RotateRefreshTokenAsync(raw);

            Assert.False(result.Success);
            Assert.Equal("User no longer exists.", result.Detail);
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_LockedUser_ReturnsFailureAndRevokesToken()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30));
        var user = await db.Users.FirstAsync();
        user.LockedUntil = FixedUtcNow.AddMinutes(30);
        await db.SaveChangesAsync();
        using (db)
        {
            var svc = CreateService(db);

            var result = await svc.RotateRefreshTokenAsync(raw);

            Assert.False(result.Success);
            Assert.Equal("Account is temporarily locked.", result.Detail);

            var hash = HashRefreshToken(raw);
            var stored = await db.RefreshTokens.FirstAsync(rt => rt.TokenHash == hash);
            Assert.True(stored.Revoked);
            Assert.Equal(0, await db.RefreshTokens.CountAsync(rt => !rt.Revoked));
        }
    }

    [Fact]
    public async Task RotateRefreshTokenAsync_ExpiredLockout_StillSucceeds()
    {
        var (db, raw) = await SeedTokenAsync(FixedUtcNow.AddDays(30));
        var user = await db.Users.FirstAsync();
        user.LockedUntil = FixedUtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
        using (db)
        {
            var svc = CreateService(db);

            var result = await svc.RotateRefreshTokenAsync(raw);

            Assert.True(result.Success);
            Assert.Equal("testuser", result.Username);
            Assert.Equal("new-access-token", result.NewAccessToken);
        }
    }

    // ── RevokeAllUserTokensAsync ────────────────────────────────────────

    [Fact]
    public async Task RevokeAllUserTokensAsync_RevokesUnrevokedTokens()
    {
        using var db = CreateDb();
        await SeedUserAsync(db);
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
        using var db = CreateDb();
        await SeedUserAsync(db);
        db.RefreshTokens.AddRange(
            new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                TokenHash = "revoked-tok",
                ExpiresAt = FixedUtcNow.AddDays(30),
                CreatedAt = FixedUtcNow,
                Revoked = true,
            },
            new RefreshToken
            {
                Id = Guid.NewGuid(),
                UserId = UserId,
                TokenHash = "active-tok",
                ExpiresAt = FixedUtcNow.AddDays(60),
                CreatedAt = FixedUtcNow,
                Revoked = false,
            });
        await db.SaveChangesAsync();
        var svc = CreateService(db);

        await svc.RevokeAllUserTokensAsync(UserId);

        // The already-revoked token is left completely untouched...
        var revoked = await db.RefreshTokens.FirstAsync(t => t.TokenHash == "revoked-tok");
        Assert.True(revoked.Revoked);
        Assert.Equal(FixedUtcNow.AddDays(30), revoked.ExpiresAt);

        // ...while the active token is revoked by the call.
        var active = await db.RefreshTokens.FirstAsync(t => t.TokenHash == "active-tok");
        Assert.True(active.Revoked);
    }

    // ── IssueRefreshTokenAsync ──────────────────────────────────────────

    [Fact]
    public async Task IssueRefreshTokenAsync_ReturnsRawHashAndExpiry()
    {
        using var db = CreateDb();
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
        using var db = CreateDb();
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

    // ── SaveUserAsync (via UserCreationService) ──────────────────────────

    [Fact]
    public async Task SaveUserAsync_PersistsNewUser()
    {
        using var db = CreateDb();
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
            SecurityStamp = User.GenerateSecurityStamp(),
        };

        var svc = new UserCreationService(db, Mock.Of<IUserAuthorizationService>(), Mock.Of<ILogger<UserCreationService>>());
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
        using var db = CreateDb();
        var user = await SeedUserAsync(db, failedLoginAttempts: 0);
        var svc = CreateService(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task RecordFailedLoginAsync_AtThreshold_LocksUser()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, securityStamp: OriginalStamp, failedLoginAttempts: 4);
        var svc = CreateService(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        Assert.Equal(5, user.FailedLoginAttempts);
        Assert.Equal(FixedUtcNowOffset.DateTime.AddMinutes(15), user.LockedUntil);
        Assert.NotEqual(OriginalStamp, user.SecurityStamp);
    }

    // ── ResetFailedAttemptsIfNeededAsync ────────────────────────────────

    [Fact]
    public async Task ResetFailedAttemptsIfNeededAsync_ClearsState()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, failedLoginAttempts: 3, lockedUntil: FixedUtcNow.AddMinutes(30));
        var svc = CreateService(db);

        await svc.ResetFailedAttemptsIfNeededAsync(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task ResetFailedAttemptsIfNeededAsync_AlreadyClean_NoSave()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, failedLoginAttempts: 0);
        var svc = CreateService(db);

        await svc.ResetFailedAttemptsIfNeededAsync(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }
}
