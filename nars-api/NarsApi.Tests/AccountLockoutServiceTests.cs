using Microsoft.EntityFrameworkCore;
using Moq;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;
using NarsApi.Services;
using static NarsApi.Tests.TestData;
using Xunit;

namespace NarsApi.Tests;

public class AccountLockoutServiceTests
{
    private const string OriginalStamp = "original-stamp";

    private static AppDbContext CreateDb() => CreateInMemoryDb("AccountLockoutTest");

    private static Task<AccountLockoutService> CreateServiceAsync(AppDbContext db) =>
        Task.FromResult<AccountLockoutService>(
            new TestableAccountLockoutService(db, Mock.Of<ISecurityStampCache>()));

    /// <summary>
    /// A testable subclass that replaces the PostgreSQL-only ExecuteUpdateAsync
    /// with a tracked-entity equivalent usable with the InMemory provider.
    /// Test-shared: AuthTestHelper builds its AuthController stack on it too.
    ///
    /// This means unit tests exercise slightly different code paths than production.
    /// Integration tests (AuthControllerServiceTests) cover the real PostgreSQL
    /// code paths via Testcontainers.
    /// </summary>
    internal sealed class TestableAccountLockoutService(
        AppDbContext db,
        ISecurityStampCache stampCache) : AccountLockoutService(db, stampCache)
    {
        private readonly AppDbContext _db = db;

        protected override async Task<bool> RecordFailedLoginInStoreAsync(
            User user, int maxFailedAttempts, int lockoutMinutes, DateTime now, CancellationToken cancellationToken)
        {
            // Mirrors the atomic production SQL on a tracked entity, since the
            // InMemory provider cannot run ExecuteUpdateAsync.
            if (user.LockedUntil.HasValue && user.LockedUntil.Value <= now)
            {
                user.FailedLoginAttempts = 0;
                user.LockedUntil = null;
            }

            user.FailedLoginAttempts++;

            if (user.FailedLoginAttempts < maxFailedAttempts)
            {
                // Persist the increment (the production SQL UPDATE is immediate).
                await _db.SaveChangesAsync(cancellationToken);
                return false;
            }

            // Threshold reached: lock the account, reset the counter so
            // re-locking after expiry needs another full sequence, and rotate
            // the security stamp.
            user.LockedUntil = now.AddMinutes(lockoutMinutes);
            user.FailedLoginAttempts = 0;
            user.SecurityStamp = User.GenerateSecurityStamp();
            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
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

    // ── RecordFailedLoginAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task RecordFailedLoginAsync_IncrementsAttempts()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, failedLoginAttempts: 0);
        var svc = await CreateServiceAsync(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task RecordFailedLoginAsync_AtThreshold_LocksUserAndResetsCounter()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, securityStamp: OriginalStamp, failedLoginAttempts: 4);
        var svc = await CreateServiceAsync(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        // Counter resets so re-locking after expiry requires a full new sequence.
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Equal(FixedUtcNowOffset.DateTime.AddMinutes(15), user.LockedUntil);
        Assert.NotEqual(OriginalStamp, user.SecurityStamp);
    }

    [Fact]
    public async Task RecordFailedLoginAsync_WhileLocked_IgnoresFailure()
    {
        using var db = CreateDb();
        var lockedUntil = FixedUtcNow.AddMinutes(10);
        var user = await SeedUserAsync(db, securityStamp: OriginalStamp, failedLoginAttempts: 0, lockedUntil: lockedUntil);
        var svc = await CreateServiceAsync(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        // An active lockout must not be extendable by further bad passwords.
        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Equal(lockedUntil, user.LockedUntil);
        Assert.Equal(OriginalStamp, user.SecurityStamp);
    }

    [Fact]
    public async Task RecordFailedLoginAsync_ExpiredLockout_StartsFreshCycle()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, securityStamp: OriginalStamp,
            failedLoginAttempts: 5, lockedUntil: FixedUtcNow.AddMinutes(-1));
        var svc = await CreateServiceAsync(db);

        await svc.RecordFailedLoginAsync(user, maxFailedAttempts: 5, lockoutMinutes: 15, FixedUtcNowOffset);

        // After the previous lockout expired, one failure must not immediately
        // re-lock; a fresh sequence of failures is required.
        Assert.Equal(1, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
        Assert.Equal(OriginalStamp, user.SecurityStamp);
    }

    // ── ResetFailedAttemptsIfNeededAsync ────────────────────────────────────────

    [Fact]
    public async Task ResetFailedAttemptsIfNeededAsync_ClearsState()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, failedLoginAttempts: 3, lockedUntil: FixedUtcNow.AddMinutes(30));
        var svc = await CreateServiceAsync(db);

        await svc.ResetFailedAttemptsIfNeededAsync(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }

    [Fact]
    public async Task ResetFailedAttemptsIfNeededAsync_AlreadyClean_NoSave()
    {
        using var db = CreateDb();
        var user = await SeedUserAsync(db, failedLoginAttempts: 0);
        var svc = await CreateServiceAsync(db);

        await svc.ResetFailedAttemptsIfNeededAsync(user);

        Assert.Equal(0, user.FailedLoginAttempts);
        Assert.Null(user.LockedUntil);
    }
}
