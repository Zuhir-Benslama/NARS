using Microsoft.EntityFrameworkCore;
using NarsApi.Data;
using NarsApi.Infrastructure;
using NarsApi.Models;

namespace NarsApi.Services;

/// <summary>
/// Tracks failed sign-in attempts and applies/clears account lockouts. Virtual
/// seam <see cref="RecordFailedLoginInStoreAsync"/> mirrors the one that lived
/// on <see cref="RefreshTokenService"/> so unit tests on the InMemory provider
/// can substitute tracked-entity storage.
/// </summary>
public class AccountLockoutService(
    AppDbContext db,
    ISecurityStampCache stampCache) : IAccountLockoutService
{
    public async Task RecordFailedLoginAsync(
        User user, int maxFailedAttempts, int lockoutMinutes, DateTimeOffset utcNow, CancellationToken cancellationToken = default)
    {
        // While a lockout is active, ignore further failures: counting them
        // would let anyone who knows a username extend the lockout indefinitely
        // with a steady trickle of bad passwords.
        var now = utcNow.UtcDateTime;
        if (user.LockedUntil.HasValue && user.LockedUntil.Value > now)
        {
            return;
        }

        // A lockout that has already expired starts a fresh counting cycle,
        // so re-locking requires another full sequence of failed attempts.
        if (await RecordFailedLoginInStoreAsync(user, maxFailedAttempts, lockoutMinutes, now, cancellationToken))
        {
            // Rotate the security stamp so any access tokens issued before the
            // lockout are rejected immediately (see AuthenticationExtensions
            // OnTokenValidated) instead of remaining valid until expiry.
            stampCache.EvictStamp(user.Id);
        }
    }

    /// <summary>
    /// Applies a failed login atomically in the database and reports whether a
    /// fresh lockout was just applied.
    ///
    /// The failed-login counter is incremented with a row-level atomic UPDATE
    /// (<c>failed_login_attempts = failed_login_attempts + 1</c>) instead of a
    /// read-modify-write on a tracked entity, so two concurrent bad-password
    /// attempts cannot both persist the same value and lose one increment
    /// (which would delay or dodge the lockout threshold). Virtual so unit
    /// tests can override it with a tracked-entity equivalent — the InMemory
    /// provider cannot execute <c>ExecuteUpdateAsync</c>.
    /// </summary>
    protected virtual async Task<bool> RecordFailedLoginInStoreAsync(
        User user, int maxFailedAttempts, int lockoutMinutes, DateTime now, CancellationToken cancellationToken)
    {
        // Clear an expired lockout so the fresh cycle starts counting from zero.
        await db.Users
            .Where(u => u.Id == user.Id && u.LockedUntil != null && u.LockedUntil <= now)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.LockedUntil, u => (DateTime?)null)
                .SetProperty(u => u.FailedLoginAttempts, 0), cancellationToken);

        // Atomic increment. Skipping rows that are currently locked keeps an
        // active lockout immune to extension by further bad passwords.
        await db.Users
            .Where(u => u.Id == user.Id && u.LockedUntil == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.FailedLoginAttempts, u => u.FailedLoginAttempts + 1), cancellationToken);

        var attempts = await db.Users.AsNoTracking()
            .Where(u => u.Id == user.Id)
            .Select(u => u.FailedLoginAttempts)
            .FirstOrDefaultAsync(cancellationToken);

        if (attempts < maxFailedAttempts)
        {
            return false;
        }

        // Threshold reached: lock the account, reset the counter so re-locking
        // after expiry needs another full sequence, and rotate the security stamp.
        var newStamp = User.GenerateSecurityStamp();
        await db.Users
            .Where(u => u.Id == user.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(u => u.LockedUntil, now.AddMinutes(lockoutMinutes))
                .SetProperty(u => u.FailedLoginAttempts, 0)
                .SetProperty(u => u.SecurityStamp, newStamp), cancellationToken);
        return true;
    }

    public async Task ResetFailedAttemptsIfNeededAsync(User user, CancellationToken cancellationToken = default)
    {
        if (user.FailedLoginAttempts > 0 || user.LockedUntil.HasValue)
        {
            user.FailedLoginAttempts = 0;
            user.LockedUntil = null;
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
